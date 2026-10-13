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

    public static void MapBundleTools(this WebApplication app)
    {
        app.MapGet("/api/bundle-tools", async (PipeClient pipe, CancellationToken ct) => Data(await pipe.Call(new("bundle-tools-status"), ct))).RequireAuthorization("bundles.read");
        app.MapPost("/api/bundle-tools/settings", async (JsonElement body, PipeClient pipe, CancellationToken ct) => Data(await pipe.Call(new("bundle-tools-settings", body.GetRawText()), ct))).RequireAuthorization("settings.write");
        app.MapPost("/api/bundle-tools/upload", (HttpContext c, PipeClient pipe, ConfigPath path, CancellationToken ct) => Upload(c, pipe, path.Value, ct)).RequireAuthorization("bundles.upload");
        app.MapPost("/api/bundle-tools/upload/install", async (JsonElement body, PipeClient pipe, CancellationToken ct) => Data(await pipe.Call(new("bundle-tools-upload-install", body.GetRawText()), ct))).RequireAuthorization("bundles.upload");
        app.MapPost("/api/bundle-tools/tools/install", async (JsonElement body, PipeClient pipe, CancellationToken ct) => Data(await pipe.Call(new("bundle-tools-tool-install", body.GetRawText()), ct))).RequireAuthorization("settings.write");
        app.MapPost("/api/bundle-tools/tools/remove", async (JsonElement body, PipeClient pipe, CancellationToken ct) => Data(await pipe.Call(new("bundle-tools-tool-remove", body.GetRawText()), ct))).RequireAuthorization("settings.write");
        app.MapGet("/api/bundle-tools/tools/latest", async (PipeClient pipe, CancellationToken ct) => Data(await pipe.Call(new("bundle-tools-tool-latest"), ct))).RequireAuthorization("settings.write");
        app.MapGet("/api/bundle-tools/projects", async (PipeClient pipe, CancellationToken ct) => Data(await pipe.Call(new("bundle-tools-projects"), ct))).RequireAuthorization("bundles.build");
        app.MapPost("/api/bundle-tools/projects/create", async (JsonElement body, PipeClient pipe, CancellationToken ct) => Data(await pipe.Call(new("bundle-tools-project-create", body.GetRawText()), ct))).RequireAuthorization("bundles.build");
        foreach (var action in new[] { "build", "rollback", "remove" })
        {
            var name = action;
            app.MapPost("/api/bundle-tools/projects/" + name, async (NameBody body, PipeClient pipe, CancellationToken ct) => Data(await pipe.Call(new("bundle-tools-project-" + name, body.Name), ct))).RequireAuthorization("bundles.build");
        }
        app.MapPost("/api/bundle-tools/packages/info", async (JsonElement body, PipeClient pipe, CancellationToken ct) => Data(await pipe.Call(new("bundle-tools-packages-info", body.GetRawText()), ct))).RequireAuthorization("bundles.build");
        app.MapPost("/api/bundle-tools/packages/set", async (JsonElement body, PipeClient pipe, CancellationToken ct) => Data(await pipe.Call(new("bundle-tools-packages-set", body.GetRawText()), ct))).RequireAuthorization("bundles.build");
        app.MapGet("/api/bundle-tools/jobs/{id}", async (string id, PipeClient pipe, CancellationToken ct) => Data(await pipe.Call(new("bundle-tools-job", id), ct))).RequireAuthorization("bundles.read");
    }
}
