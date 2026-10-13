using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using ReplayPanel.Shared;
using ReplayPanel.Web;

if(Environment.GetEnvironmentVariable("REPLAY_PANEL_LOG_DIR") is {} logDirectory){
 Directory.CreateDirectory(logDirectory);
 Console.SetOut(new StreamWriter(new FileStream(Path.Combine(logDirectory,"web.log"),FileMode.Append,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete)){AutoFlush=true});
 Console.SetError(new StreamWriter(new FileStream(Path.Combine(logDirectory,"web.err.log"),FileMode.Append,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete)){AutoFlush=true});
}
var configPath = Path.GetFullPath(Environment.GetEnvironmentVariable("REPLAY_PANEL_CONFIG") ?? ".local/panel.json");
var config = PanelConfig.Load(configPath);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(config.Url);
builder.Services.AddSingleton(config);
builder.Services.AddSingleton<PipeClient>();
builder.Services.AddSingleton<BundleManager>();
builder.Services.AddHostedService<LocalStopService>();
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Path.GetDirectoryName(configPath)!, "keys"))).SetApplicationName("ReplayPanel");
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.SecurePolicy = config.PublicUrl is null ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always; o.Cookie.Name = "replay-panel-session"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict;
    o.ExpireTimeSpan = TimeSpan.FromHours(4); o.SlidingExpiration = false;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return c.Response.WriteAsJsonAsync(new {error="auth.denied"}); };
    o.Events.OnValidatePrincipal = async c => {
        var user=await CurrentUser(c.Principal!,c.HttpContext.RequestServices.GetRequiredService<PipeClient>(),c.HttpContext.RequestAborted);
        if(user is null){c.RejectPrincipal();await c.HttpContext.SignOutAsync();}
        else c.ReplacePrincipal(Principal(user));
    };
});
builder.Services.AddAuthorization(o=>{foreach(var permission in Permissions.All)o.AddPolicy(permission,p=>p.RequireAuthenticatedUser().RequireClaim("permission",permission));});
builder.Services.AddAntiforgery(o => { o.HeaderName = "X-CSRF"; o.Cookie.SameSite = SameSiteMode.Strict; });
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o => {
    o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    foreach(var proxy in config.TrustedProxies) o.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
});
var app = builder.Build();
if(config.TrustedProxies.Length>0)app.UseForwardedHeaders();
var uri = new Uri(config.Url);
app.UseAuthentication();
app.Use(async (context, next) =>
{
    var host = context.Request.Host;
    if (!config.AllowedHosts.Contains(host.Value, StringComparer.OrdinalIgnoreCase) && (host.Port != uri.Port || (host.Host != "127.0.0.1" && host.Host != "localhost" && host.Host != "[::1]")))
    { context.Response.StatusCode = 400; return; }
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self' data:; font-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    if (HttpMethods.IsPost(context.Request.Method))
    {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "csrf.invalid" }); return; }
    }
    if(HttpMethods.IsPost(context.Request.Method)&&context.Request.Path.StartsWithSegments("/api")&&context.Request.Path!="/api/login"&&context.Request.Path!="/api/logout"&&context.User.Identity?.IsAuthenticated==true){
        var update=await context.RequestServices.GetRequiredService<PipeClient>().Call(new("update-status"),context.RequestAborted);
        if(update.Ok&&update.Lines is {Length:>0}){using var state=JsonDocument.Parse(update.Lines[0]);if(state.RootElement.TryGetProperty("job",out var job)&&job.ValueKind!=JsonValueKind.Null&&job.GetProperty("state").GetString()=="applying"){context.Response.StatusCode=409;await context.Response.WriteAsJsonAsync(new{error="update.busy"});return;}}
    }
    await next();
});
app.UseRateLimiter(); app.UseAuthorization();
app.UseDefaultFiles(); app.UseStaticFiles();
app.MapGet("/api/session", (HttpContext c, IAntiforgery antiforgery) =>
    Results.Ok(new { authenticated = c.User.Identity?.IsAuthenticated == true, username=c.User.Identity?.Name, permissions=c.User.FindAll("permission").Select(x=>x.Value), csrf = antiforgery.GetAndStoreTokens(c).RequestToken }));
app.MapGet("/api/bootstrap", async (PipeClient pipe, CancellationToken ct) => Data(await pipe.Call(new("bootstrap"), ct)));
app.MapGet("/api/setup/console", async (PipeClient pipe, CancellationToken ct) => Data(await pipe.Call(new("setup-banner"), ct)));
foreach(var route in new[]{"validate","install","finish"}) {
    var action=route;
    app.MapPost("/api/setup/"+route,async (JsonElement body,PipeClient pipe,CancellationToken ct)=>Data(await pipe.Call(new("setup-"+action,body.GetRawText()),ct)));
}
app.MapPost("/api/setup/pick",async (JsonElement body,HttpContext c,PipeClient pipe,CancellationToken ct)=>System.Net.IPAddress.IsLoopback(c.Connection.RemoteIpAddress??System.Net.IPAddress.None)?Data(await pipe.Call(new("setup-pick",body.GetRawText()),ct)):Results.Json(new Reply(false,"pick.unavailable"),statusCode:409));
app.MapPost("/api/setup/pick-status",async (SetupKey body,PipeClient pipe,CancellationToken ct)=>Data(await pipe.Call(new("setup-pick-status",body.Key),ct)));
app.MapPost("/api/setup/job",async (SetupKey body,PipeClient pipe,CancellationToken ct)=>Data(await pipe.Call(new("setup-job",body.Key),ct)));
app.MapPost("/api/setup/cancel",async (SetupKey body,PipeClient pipe,CancellationToken ct)=>Data(await pipe.Call(new("setup-cancel",body.Key),ct)));
app.MapPost("/api/login", async (Login body, HttpContext c, PipeClient pipe) =>
{
    if (body.Password is null || body.Password.Length > 256) return Results.Unauthorized();
    var reply = await pipe.Call(new("authenticate", JsonSerializer.Serialize(new[]{body.Username??"admin",body.Password})));
    if(!reply.Ok)return Results.Unauthorized();
    var user=JsonSerializer.Deserialize<UserView>(reply.Lines![0],PanelConfig.Json)!;
    await c.SignInAsync(Principal(user),body.Remember==true?new AuthenticationProperties{IsPersistent=true,ExpiresUtc=DateTimeOffset.UtcNow.AddDays(30)}:new AuthenticationProperties());
    return Results.Ok(new { ok = true });
}).RequireRateLimiting("login");
app.MapPost("/api/logout", async (HttpContext c) => { await c.SignOutAsync(); return Results.Ok(new { ok = true }); }).RequireAuthorization();
app.MapGet("/api/status", async (PipeClient pipe, CancellationToken ct) => Result(await pipe.Call(new("status"), ct))).RequireAuthorization();
app.MapGet("/api/logs", async (PipeClient pipe, CancellationToken ct) => Result(await pipe.Call(new("logs"), ct))).RequireAuthorization("logs.read");
app.MapGet("/api/bridge",async(PipeClient pipe,CancellationToken ct)=>Data(await pipe.Call(new("bridge-status"),ct))).RequireAuthorization();
app.MapPost("/api/bridge/settings",async(BridgeSettings body,PipeClient pipe,CancellationToken ct)=>Result(await pipe.Call(new("bridge-settings",JsonSerializer.Serialize(body,PanelConfig.Json)),ct))).RequireAuthorization("settings.write");
app.MapGet("/api/diagnostics",async(HttpContext context,PipeClient pipe,CancellationToken ct)=>{
    var q=context.Request.Query;var source=q["source"].FirstOrDefault()??"all";
    if(source=="console"?!Can(context,"console.read"):source is "log" or "panel"?!Can(context,"logs.read"):!Can(context,"console.read")||!Can(context,"logs.read"))return Results.Forbid();
    if(!long.TryParse(q["cursor"].FirstOrDefault()??"0",out var cursor)||!int.TryParse(q["limit"].FirstOrDefault()??"500",out var limit))return Results.BadRequest(new{error="diagnostics.invalid"});
    return Data(await pipe.Call(new("diagnostics",JsonSerializer.Serialize(new DiagnosticQuery(cursor,source,q["module"].FirstOrDefault(),q["search"].FirstOrDefault(),limit,q["history"]=="true"),PanelConfig.Json)),ct));
}).RequireAuthorization();
app.MapGet("/api/log-files",(HttpContext c)=>{
    var dir=LogsDirectory(configPath);
    var files=Directory.Exists(dir)?new DirectoryInfo(dir).EnumerateFiles().OrderByDescending(f=>f.LastWriteTimeUtc).Select(f=>new{name=f.Name,size=f.Length,modified=f.LastWriteTimeUtc,started=LogStart(f)}).ToArray():[];
    return Results.Json(files);
}).RequireAuthorization("logs.read");
app.MapGet("/api/log-files/{name}",(string name)=>{
    var file=SafeLog(configPath,name);if(file is null)return Results.NotFound();
    using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
    const int limit=2*1024*1024;if(stream.Length>limit)stream.Seek(-limit,SeekOrigin.End);
    using var reader=new StreamReader(stream);return Results.Text(reader.ReadToEnd(),"text/plain; charset=utf-8");
}).RequireAuthorization("logs.read");
app.MapGet("/api/log-files.zip",async(HttpContext c,CancellationToken ct)=>{
    var dir=LogsDirectory(configPath);if(!Directory.Exists(dir))return Results.NotFound();
    c.Response.ContentType="application/zip";c.Response.Headers.ContentDisposition="attachment; filename=\"blackice-server-logs-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".zip\"";
    using var zip=new System.IO.Compression.ZipArchive(c.Response.BodyWriter.AsStream(),System.IO.Compression.ZipArchiveMode.Create,true);
    foreach(var f in new DirectoryInfo(dir).EnumerateFiles().OrderBy(f=>f.Name)){
        ct.ThrowIfCancellationRequested();
        try{using var input=new FileStream(f.FullName,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);var entry=zip.CreateEntry(f.Name,System.IO.Compression.CompressionLevel.Optimal);entry.LastWriteTime=f.LastWriteTimeUtc;using var output=entry.Open();await input.CopyToAsync(output,ct);}catch(IOException){}
    }
    return Results.Empty;
}).RequireAuthorization("logs.read");
app.MapGet("/api/bundles", async (BundleManager bundles, CancellationToken ct) => Results.Ok(await bundles.List(ct))).RequireAuthorization("bundles.read");
app.MapPost("/api/bundles/{name}/{action}", async (string name, string action, HttpContext c, BundleManager bundles, CancellationToken ct) =>
{
    if(action is not ("start" or "stop" or "restart"))return Results.Json(new{error="bundle.invalid"},statusCode:409);
    if(!Can(c,"bundles."+action))return Results.Forbid();
    try { var result=await bundles.Action(name,action,ct);return result.Ok ? Results.Ok(result) : Results.Json(result,statusCode:409); }
    catch (InvalidOperationException e) { return Results.Json(new { error = e.Message }, statusCode: 409); }
}).RequireAuthorization("bundles.read");
app.MapPost("/api/server/{action}", async (string action, HttpContext c, BundleManager bundles, CancellationToken ct) =>
{
    if (action is not ("start" or "stop" or "restart" or "force-stop")) return Results.BadRequest();
    if(!Can(c,"server."+action))return Results.Forbid();
    return Result(await bundles.Command(new(action), ct));
}).RequireAuthorization();
app.MapPost("/api/command", async (Command body, BundleManager bundles, CancellationToken ct) =>
    Result(await bundles.Command(new("command", body.Text), ct))).RequireAuthorization("console.write");
app.MapPost("/api/settings", async (Settings body, HttpContext c, PipeClient pipe, CancellationToken ct) => {
    if((body.ShutdownMode is not null||body.AutoStartServer is not null)&&!Can(c,"settings.write"))return Results.Forbid();
    return Result(await pipe.Call(new("settings", ShutdownMode: body.ShutdownMode,AutoStartServer:body.AutoStartServer,Locale:body.Locale), ct));
}).RequireAuthorization();
app.MapGet("/api/server-name",async(PipeClient pipe,CancellationToken ct)=>{
    var reply=await pipe.Call(new("server-config"),ct);
    if(!reply.Ok||reply.Lines is not {Length:>0})return Results.Ok(new{name=""});
    using var doc=JsonDocument.Parse(reply.Lines[0]);
    return Results.Ok(new{name=doc.RootElement.GetProperty("values").GetProperty("name").GetString()??""});
}).RequireAuthorization();
app.MapPost("/api/server-path",async(ServerPath body,PipeClient pipe,CancellationToken ct)=>Result(await pipe.Call(new("server-path",body.ServerRoot),ct))).RequireAuthorization("settings.write");
app.MapGet("/api/server-config",async(PipeClient pipe,CancellationToken ct)=>Data(await pipe.Call(new("server-config"),ct))).RequireAuthorization("settings.write");
app.MapPost("/api/server-config",async(JsonElement body,PipeClient pipe,CancellationToken ct)=>Data(await pipe.Call(new("server-config-save",body.GetRawText()),ct))).RequireAuthorization("settings.write");
app.MapGet("/api/updates",async(PipeClient pipe,CancellationToken ct)=>Data(await pipe.Call(new("update-status"),ct))).RequireAuthorization("settings.write");
app.MapGet("/api/server-updates",async(PipeClient pipe,CancellationToken ct)=>Data(await pipe.Call(new("server-update-status"),ct))).RequireAuthorization("settings.write");
foreach(var serverAction in new[]{"check","download","cancel","apply"}){var action=serverAction;var endpoint=app.MapPost("/api/server-updates/"+action,async(JsonElement body,PipeClient pipe,CancellationToken ct)=>Data(await pipe.Call(new("server-update-"+action,body.TryGetProperty("target",out var target)?target.GetString():null),ct))).RequireAuthorization("settings.write");if(action is "apply" or "download")endpoint.RequireAuthorization("users.manage");}
foreach(var updateAction in new[]{"check","download","cancel","apply"}){var action=updateAction;var endpoint=app.MapPost("/api/updates/"+action,async(PipeClient pipe,CancellationToken ct)=>Data(await pipe.Call(new("update-"+action),ct))).RequireAuthorization("settings.write");if(action is "apply" or "download")endpoint.RequireAuthorization("users.manage");}
app.MapGet("/api/users",async(PipeClient pipe,CancellationToken ct)=>Data(await pipe.Call(new("users"),ct))).RequireAuthorization("users.manage");
app.MapPost("/api/users/save",async(UserChange body,PipeClient pipe,CancellationToken ct)=>Result(await pipe.Call(new("user-save",JsonSerializer.Serialize(body,PanelConfig.Json)),ct))).RequireAuthorization("users.manage");
app.MapPost("/api/users/delete",async(UserDelete body,PipeClient pipe,CancellationToken ct)=>Result(await pipe.Call(new("user-delete",body.Username),ct))).RequireAuthorization("users.manage");
app.MapGet("/api/events", async (HttpContext c, PipeClient pipe) =>
{
    c.Response.ContentType = "text/event-stream";
    using var stopping = CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted, app.Lifetime.ApplicationStopping);
    var ct = stopping.Token;
    try {
        while (!ct.IsCancellationRequested)
        {
            var user=await CurrentUser(c.User,pipe,ct);if(user is null)break;
            var status = await pipe.Call(new("status"), ct);
            var console = user.Permissions.Contains("console.read")?await pipe.Call(new("console"), ct):new Reply(true,Lines:[]);
            await c.Response.WriteAsync("data: " + JsonSerializer.Serialize(new { status, console, permissions=user.Permissions }, PanelConfig.Json) + "\n\n", ct);
            await c.Response.Body.FlushAsync(ct);
            await Task.Delay(1000, ct);
        }
    } catch (OperationCanceledException) { } catch (IOException) { }
}).RequireAuthorization();
app.MapFallbackToFile("index.html");
app.Lifetime.ApplicationStopping.Register(() =>
{
    try { app.Services.GetRequiredService<PipeClient>().Call(new("webhost-stopping")).GetAwaiter().GetResult(); }
    catch (Exception) { app.Logger.LogWarning("Supervisor could not be notified during webhost shutdown."); }
});
await app.RunAsync();
static string LogsDirectory(string configPath)=>Path.Combine(PanelConfig.Load(configPath).ServerRoot,"logs");
static string? SafeLog(string configPath,string name){
    if(!System.Text.RegularExpressions.Regex.IsMatch(name,@"\A[A-Za-z0-9._-]{1,128}\z"))return null;
    var path=Path.Combine(LogsDirectory(configPath),name);return File.Exists(path)?path:null;
}
static DateTime? LogStart(FileInfo f){
    var m=System.Text.RegularExpressions.Regex.Match(f.Name,@"(\d{4}-\d{2}-\d{2})T(\d{2})-(\d{2})-(\d{2})Z");
    return m.Success&&DateTime.TryParse($"{m.Groups[1].Value}T{m.Groups[2].Value}:{m.Groups[3].Value}:{m.Groups[4].Value}Z",null,System.Globalization.DateTimeStyles.AdjustToUniversal|System.Globalization.DateTimeStyles.AssumeUniversal,out var d)?d:null;
}
static IResult Data(Reply reply) => reply.Ok ? Results.Ok(reply.Lines is {Length:>0} ? JsonSerializer.Deserialize<JsonElement>(reply.Lines[0]) : JsonSerializer.SerializeToElement(new{ok=true})) : Results.Json(reply,statusCode:409);
static IResult Result(Reply reply) => reply.Ok ? Results.Ok(reply) : Results.Json(reply, statusCode: 409);
static bool Can(HttpContext c,string permission)=>c.User.HasClaim("permission",permission);
static ClaimsPrincipal Principal(UserView user)=>new(new ClaimsIdentity(new[]{new Claim(ClaimTypes.Name,user.Username),new Claim("stamp",user.Stamp)}.Concat(user.Permissions.Select(p=>new Claim("permission",p))),CookieAuthenticationDefaults.AuthenticationScheme));
static async Task<UserView?> CurrentUser(ClaimsPrincipal principal,PipeClient pipe,CancellationToken ct){
    try{var reply=await pipe.Call(new("account-session",JsonSerializer.Serialize(new[]{principal.Identity?.Name??"",principal.FindFirst("stamp")?.Value??""})),ct);return reply.Ok?JsonSerializer.Deserialize<UserView>(reply.Lines![0],PanelConfig.Json):null;}catch(IOException){return null;}
}
record UserDelete(string Username);
record Login(string? Password, string? Username, bool? Remember);
record Command(string? Text);
record Settings(string? ShutdownMode, bool? AutoStartServer, string? Locale);
public partial class Program { }

record SetupKey(string Key);

record ServerPath(string ServerRoot);
