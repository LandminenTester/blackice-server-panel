using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using ReplayPanel.Shared;
namespace ReplayPanel.Web;

public sealed record ConfigPath(string Value);
public sealed record NameBody(string Name);

/// <summary>HTTP surface of the EXPERIMENTAL bundle tools. Thin proxy: the supervisor owns all logic and enforces the master switch.</summary>
public static class BundleToolsEndpoints
{
    /// <summary>Streams the uploaded ZIP to the private state folder (the supervisor installs it; the web host never writes into the game server).</summary>
    private static async Task<IResult> Upload(HttpContext c, PipeClient pipe, string configPath, CancellationToken ct)
    {
        var settings = PanelConfig.Load(configPath).BundleTools;
        if (!settings.Enabled || !settings.UploadEnabled) return Results.Json(new Reply(false, "bundleTools.disabled"), statusCode: 409);
        var limit = settings.MaxUploadMb * 1024L * 1024L;
        if (c.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature) feature.MaxRequestBodySize = limit + 1024 * 1024;
        if (!MediaTypeHeaderValue.TryParse(c.Request.ContentType, out var media) || !media.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase) || media.Boundary.Length == 0)
            return Results.Json(new Reply(false, "bundleTools.upload"), statusCode: 409);
        var directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath))!, "uploads");
        Directory.CreateDirectory(directory);
        var id = Guid.NewGuid().ToString("N");
        var path = Path.Combine(directory, id + ".zip");
        string? original = null;
        try
        {
            var reader = new MultipartReader(media.Boundary.Value!, c.Request.Body) { BodyLengthLimit = limit + 1024 * 1024, HeadersLengthLimit = 16 * 1024 };
            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync(ct)) is not null)
            {
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition) || !disposition.IsFileDisposition()) continue;
                original = Path.GetFileName(disposition.FileName.Value ?? disposition.FileNameStar.Value ?? "bundle.zip");
                await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await section.Body.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;
                    if (total > limit) throw new InvalidOperationException("bundleTools.uploadSize");
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                break;
            }
            if (original is null || !File.Exists(path) || new FileInfo(path).Length < 22) throw new InvalidOperationException("bundleTools.upload");
            using (var probe = File.OpenRead(path)) { var magic = new byte[2]; if (probe.Read(magic) < 2 || magic[0] != (byte)'P' || magic[1] != (byte)'K') throw new InvalidOperationException("bundleTools.zipInvalid"); }
            var reply = await pipe.Call(new("bundle-tools-upload-inspect", JsonSerializer.Serialize(new { file = id + ".zip", originalName = original }, PanelConfig.Json)), ct);
            if (!reply.Ok) { TryDelete(path); return Results.Json(reply, statusCode: 409); }
            return Data(reply);
        }
        catch (InvalidOperationException e)
        {
            TryDelete(path);
            return Results.Json(new Reply(false, e.Message.StartsWith("bundleTools.") ? e.Message : "bundleTools.upload"), statusCode: 409);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or BadHttpRequestException)
        {
            TryDelete(path);
            return Results.Json(new Reply(false, "bundleTools.upload"), statusCode: 409);
        }
    }

    private static void TryDelete(string path) { try { File.Delete(path); } catch (IOException) { } }

    private static IResult Data(Reply reply) => reply.Ok
        ? Results.Ok(reply.Lines is { Length: > 0 } ? JsonSerializer.Deserialize<JsonElement>(reply.Lines[0]) : JsonSerializer.SerializeToElement(new { ok = true }))
        : Results.Json(reply, statusCode: 409);

    private static bool CanJob(HttpContext c,string? kind)=>kind switch {
        "tool-install"=>Can(c,"bundles.tools"),"upload"=>Can(c,"bundles.upload"),"bundle-delete"=>Can(c,"bundles.delete"),
        "git-review"=>Can(c,"bundles.build")&&Can(c,"bundles.review"),"git-merge"=>Can(c,"bundles.build")&&Can(c,"bundles.merge"),
        "package-review"=>Can(c,"bundles.build")&&Can(c,"bundles.packages")&&Can(c,"bundles.review"),_=>Can(c,"bundles.build")};
    private static bool Can(HttpContext c,string permission)=>c.User.HasClaim("permission",permission);
    private static async Task<IResult> Status(HttpContext c,PipeClient pipe,CancellationToken ct){
        if(!new[]{"bundles.read","bundles.settings","bundles.tools","bundles.accounts.manage"}.Any(p=>Can(c,p)))return Results.Forbid();
        var reply=await pipe.Call(new("bundle-tools-status"),ct);if(!reply.Ok)return Data(reply);
        var body=System.Text.Json.Nodes.JsonNode.Parse(reply.Lines![0])!.AsObject();
        if(!Can(c,"bundles.accounts.use")&&!Can(c,"bundles.accounts.manage"))body["accounts"]=new System.Text.Json.Nodes.JsonArray();
        bool Allowed(System.Text.Json.Nodes.JsonNode? job){if(job is null)return false;var kind=job["kind"]?.GetValue<string>();return CanJob(c,kind);}
        if(body["jobs"] is System.Text.Json.Nodes.JsonArray jobs)body["jobs"]=new System.Text.Json.Nodes.JsonArray(jobs.Where(Allowed).Select(j=>j!.DeepClone()).ToArray());
        if(!Allowed(body["job"]))body.Remove("job");
        if(!new[]{"bundles.build","bundles.upload","bundles.tools","bundles.delete"}.Any(p=>Can(c,p)))body.Remove("jobs");return Results.Ok(body);
    }
    public static void MapBundleTools(this WebApplication app){
        app.MapGet("/api/bundle-tools",Status).RequireAuthorization();
        app.MapPost("/api/bundle-tools/settings",async(HttpContext c,JsonElement body,PipeClient pipe,CancellationToken ct)=>{var reply=await pipe.Call(new("bundle-tools-settings",body.GetRawText()),ct);return reply.Ok?await Status(c,pipe,ct):Data(reply);}).RequireAuthorization("bundles.settings");
        app.MapPost("/api/bundle-tools/upload",(HttpContext c,PipeClient pipe,ConfigPath path,CancellationToken ct)=>Upload(c,pipe,path.Value,ct)).RequireAuthorization("bundles.upload");
        app.MapPost("/api/bundle-tools/upload/exists",async(NameBody body,PipeClient pipe,CancellationToken ct)=>Data(await pipe.Call(new("bundle-tools-upload-exists",body.Name),ct))).RequireAuthorization("bundles.upload");
        app.MapPost("/api/bundle-tools/upload/install",async(JsonElement body,PipeClient pipe,CancellationToken ct)=>Data(await pipe.Call(new("bundle-tools-upload-install",body.GetRawText()),ct))).RequireAuthorization("bundles.upload");
        foreach(var action in new[]{"install","remove"}){var a=action;app.MapPost("/api/bundle-tools/tools/"+a,async(HttpContext c,JsonElement body,PipeClient pipe,CancellationToken ct)=>{var reply=await pipe.Call(new("bundle-tools-tool-"+a,body.GetRawText()),ct);return a=="remove"&&reply.Ok?await Status(c,pipe,ct):Data(reply);}).RequireAuthorization("bundles.tools");}
        app.MapGet("/api/bundle-tools/tools/latest",async(PipeClient pipe,CancellationToken ct)=>Data(await pipe.Call(new("bundle-tools-tool-latest"),ct))).RequireAuthorization("bundles.tools");
        app.MapGet("/api/bundle-tools/projects",async(PipeClient pipe,CancellationToken ct)=>Data(await pipe.Call(new("bundle-tools-projects"),ct))).RequireAuthorization("bundles.build");
        foreach(var action in new[]{"create","import","pull","checkout"}){var a=action;app.MapPost("/api/bundle-tools/projects/"+a,async(HttpContext c,JsonElement body,PipeClient pipe,CancellationToken ct)=>{if(a=="import"&&body.TryGetProperty("accountId",out var account)&&account.ValueKind==JsonValueKind.String&&!string.IsNullOrEmpty(account.GetString())&&!Can(c,"bundles.accounts.use"))return Results.Forbid();if(a=="create"&&body.TryGetProperty("repo",out var repo)&&repo.ValueKind==JsonValueKind.Object&&(!Can(c,"bundles.git.write")||!Can(c,"bundles.accounts.use")))return Results.Forbid();return Data(await pipe.Call(new("bundle-tools-project-"+a,body.GetRawText(),UseAccounts:Can(c,"bundles.accounts.use")),ct));}).RequireAuthorization("bundles.build");}
        foreach(var action in new[]{"check","refs"}){var a=action;app.MapPost("/api/bundle-tools/projects/"+a,async(HttpContext c,NameBody body,PipeClient pipe,CancellationToken ct)=>Data(await pipe.Call(new("bundle-tools-project-"+a,body.Name,UseAccounts:Can(c,"bundles.accounts.use")),ct))).RequireAuthorization("bundles.build");}
        app.MapPost("/api/bundle-tools/accounts/save",async(JsonElement body,PipeClient pipe,CancellationToken ct)=>Data(await pipe.Call(new("bundle-tools-account-save",body.GetRawText()),ct))).RequireAuthorization("bundles.accounts.manage");
        foreach(var action in new[]{"delete","test"}){var a=action;app.MapPost("/api/bundle-tools/accounts/"+a,async(NameBody body,PipeClient pipe,CancellationToken ct)=>Data(await pipe.Call(new("bundle-tools-account-"+a,body.Name),ct))).RequireAuthorization("bundles.accounts.manage");}
        foreach(var action in new[]{"build","rollback","remove"}){var a=action;app.MapPost("/api/bundle-tools/projects/"+a,async(NameBody body,PipeClient pipe,CancellationToken ct)=>Data(await pipe.Call(new("bundle-tools-project-"+a,body.Name),ct))).RequireAuthorization("bundles.build",a=="remove"?"bundles.sources.delete":"bundles.build");}
        app.MapPost("/api/bundle-tools/packages/set",()=>Results.Json(new{error="bundleTools.workflow"},statusCode:409)).RequireAuthorization("bundles.build");
        foreach(var action in new[]{"info"}){var a=action;app.MapPost("/api/bundle-tools/packages/"+a,async(JsonElement body,PipeClient pipe,CancellationToken ct)=>Data(await pipe.Call(new("bundle-tools-packages-"+a,body.GetRawText()),ct))).RequireAuthorization("bundles.build");}
        foreach(var action in new[]{"owners","repositories"}){var a=action;app.MapPost("/api/bundle-tools/remote/"+a,async(JsonElement body,PipeClient pipe,CancellationToken ct)=>Data(await pipe.Call(new("bundle-tools-remote-"+a,body.GetRawText()),ct))).RequireAuthorization("bundles.accounts.use").RequireRateLimiting("bundle-remote");}
        app.MapPost("/api/bundle-tools/projects/workflow",async(JsonElement body,PipeClient pipe,CancellationToken ct)=>Data(await pipe.Call(new("bundle-tools-workflow",body.GetRawText()),ct))).RequireAuthorization("bundles.settings");
        app.MapPost("/api/bundle-tools/projects/delivery",()=>Results.Json(new{error="bundleTools.workflow"},statusCode:409)).RequireAuthorization("bundles.build","bundles.git.write","bundles.accounts.use");
        app.MapPost("/api/bundle-tools/projects/compare",async(JsonElement body,PipeClient pipe,CancellationToken ct)=>Data(await pipe.Call(new("bundle-tools-compare",body.GetRawText()),ct))).RequireAuthorization("bundles.build","bundles.accounts.use");
        foreach(var action in new[]{"merge","review"}){var a=action;app.MapPost("/api/bundle-tools/projects/"+a,async(JsonElement body,PipeClient pipe,CancellationToken ct)=>Data(await pipe.Call(new("bundle-tools-"+a,body.GetRawText()),ct))).RequireAuthorization("bundles.build","bundles.git.write","bundles.accounts.use",a=="merge"?"bundles.merge":"bundles.review");}
        app.MapPost("/api/bundle-tools/packages/review",async(JsonElement body,PipeClient pipe,CancellationToken ct)=>Data(await pipe.Call(new("bundle-tools-package-review",body.GetRawText()),ct))).RequireAuthorization("bundles.build","bundles.git.write","bundles.accounts.use","bundles.packages","bundles.review");
        app.MapGet("/api/bundles/metadata",async(PipeClient pipe,CancellationToken ct)=>Data(await pipe.Call(new("bundle-metadata"),ct))).RequireAuthorization("bundles.read");
        foreach(var action in new[]{"tags","autostart"}){var a=action;app.MapPost("/api/bundles/"+a,async(JsonElement body,PipeClient pipe,CancellationToken ct)=>Data(await pipe.Call(new("bundle-"+a,body.GetRawText()),ct))).RequireAuthorization("bundles.read","bundles."+a);}
        app.MapPost("/api/bundles/delete-info",async(NameBody body,PipeClient pipe,CancellationToken ct)=>Data(await pipe.Call(new("bundle-delete-info",body.Name),ct))).RequireAuthorization("bundles.read","bundles.delete");
        app.MapPost("/api/bundles/delete",async(HttpContext c,JsonElement body,PipeClient pipe,CancellationToken ct)=>{foreach(var flag in new[]{("sources","bundles.sources.delete"),("data","bundles.data.delete"),("backups","bundles.delete")})if(body.TryGetProperty(flag.Item1,out var value)&&value.ValueKind==JsonValueKind.True&&!Can(c,flag.Item2))return Results.Forbid();return Data(await pipe.Call(new("bundle-delete",body.GetRawText()),ct));}).RequireAuthorization("bundles.read","bundles.delete","bundles.stop");
        app.MapGet("/api/bundle-tools/jobs/{id}",async(string id,HttpContext c,PipeClient pipe,CancellationToken ct)=>{
            if(!new[]{"bundles.build","bundles.upload","bundles.tools","bundles.delete"}.Any(p=>Can(c,p)))return Results.Forbid();
            var reply=await pipe.Call(new("bundle-tools-job",id),ct);if(!reply.Ok)return Data(reply);
            using var doc=JsonDocument.Parse(reply.Lines![0]);var kind=doc.RootElement.GetProperty("job").GetProperty("kind").GetString();
            return CanJob(c,kind)?Data(reply):Results.Forbid();
        }).RequireAuthorization();
    }
}
