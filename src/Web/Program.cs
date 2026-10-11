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

var configPath = Path.GetFullPath(Environment.GetEnvironmentVariable("REPLAY_PANEL_CONFIG") ?? ".local/panel.json");
var config = PanelConfig.Load(configPath);
if (config.PasswordHash.Length == 0 || config.PasswordSalt.Length == 0) throw new InvalidOperationException("Run scripts/setup.ps1 to set a panel password.");
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(config.Url);
builder.Services.AddSingleton(config);
builder.Services.AddSingleton<PipeClient>();
builder.Services.AddSingleton<BundleManager>();
builder.Services.AddHostedService<LocalStopService>();
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Path.GetDirectoryName(configPath)!, "keys"))).SetApplicationName("ReplayPanel");
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "replay-panel-session"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict;
    o.ExpireTimeSpan = TimeSpan.FromHours(4); o.SlidingExpiration = false;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(o => { o.HeaderName = "X-CSRF"; o.Cookie.SameSite = SameSiteMode.Strict; });
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
var uri = new Uri(config.Url);
app.UseAuthentication();
app.Use(async (context, next) =>
{
    var host = context.Request.Host;
    if (host.Port != uri.Port || (host.Host != "127.0.0.1" && host.Host != "localhost" && host.Host != "[::1]"))
    { context.Response.StatusCode = 400; return; }
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self' data:; font-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    if (HttpMethods.IsPost(context.Request.Method))
    {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "Ungültiger Formularschutz. Seite neu laden." }); return; }
    }
    await next();
});
app.UseRateLimiter(); app.UseAuthorization();
app.UseDefaultFiles(); app.UseStaticFiles();
app.MapGet("/api/session", (HttpContext c, IAntiforgery antiforgery) =>
    Results.Ok(new { authenticated = c.User.Identity?.IsAuthenticated == true, csrf = antiforgery.GetAndStoreTokens(c).RequestToken }));
app.MapPost("/api/login", async (Login body, HttpContext c) =>
{
    if (body.Password is null || body.Password.Length > 256) return Results.Unauthorized();
    var hash = Rfc2898DeriveBytes.Pbkdf2(body.Password, Convert.FromBase64String(config.PasswordSalt), 210000, HashAlgorithmName.SHA256, 32);
    if (!CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(config.PasswordHash))) return Results.Unauthorized();
    await c.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "administrator")], CookieAuthenticationDefaults.AuthenticationScheme)));
    return Results.Ok(new { ok = true });
}).RequireRateLimiting("login");
app.MapPost("/api/logout", async (HttpContext c) => { await c.SignOutAsync(); return Results.Ok(new { ok = true }); }).RequireAuthorization();
app.MapGet("/api/status", async (PipeClient pipe, CancellationToken ct) => Result(await pipe.Call(new("status"), ct))).RequireAuthorization();
app.MapGet("/api/logs", async (PipeClient pipe, CancellationToken ct) => Result(await pipe.Call(new("logs"), ct))).RequireAuthorization();
app.MapGet("/api/bundles", async (BundleManager bundles, CancellationToken ct) => Results.Ok(await bundles.List(ct))).RequireAuthorization();
app.MapPost("/api/bundles/{name}/{action}", async (string name, string action, BundleManager bundles, CancellationToken ct) =>
{
    try { return Results.Ok(await bundles.Action(name, action, ct)); }
    catch (InvalidOperationException e) { return Results.Json(new { error = e.Message }, statusCode: 409); }
}).RequireAuthorization();
app.MapPost("/api/server/{action}", async (string action, BundleManager bundles, CancellationToken ct) =>
{
    if (action is not ("start" or "stop" or "restart" or "force-stop")) return Results.BadRequest();
    return Result(await bundles.Command(new(action), ct));
}).RequireAuthorization();
app.MapPost("/api/command", async (Command body, BundleManager bundles, CancellationToken ct) =>
    Result(await bundles.Command(new("command", body.Text), ct))).RequireAuthorization();
app.MapPost("/api/settings", async (Settings body, PipeClient pipe, CancellationToken ct) =>
    Result(await pipe.Call(new("settings", ShutdownMode: body.ShutdownMode), ct))).RequireAuthorization();
app.MapGet("/api/events", async (HttpContext c, PipeClient pipe) =>
{
    c.Response.ContentType = "text/event-stream";
    using var stopping = CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted, app.Lifetime.ApplicationStopping);
    var ct = stopping.Token;
    try {
        while (!ct.IsCancellationRequested)
        {
            var status = await pipe.Call(new("status"), ct);
            var console = await pipe.Call(new("console"), ct);
            await c.Response.WriteAsync("data: " + JsonSerializer.Serialize(new { status, console }, PanelConfig.Json) + "\n\n", ct);
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
static IResult Result(Reply reply) => reply.Ok ? Results.Ok(reply) : Results.Json(reply, statusCode: 409);
record Login(string? Password);
record Command(string? Text);
record Settings(string? ShutdownMode);
public partial class Program { }
