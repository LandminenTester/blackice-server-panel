using System.Text.Json;
using ReplayPanel.Shared;
namespace ReplayPanel.Web;

/// <summary>HTTP surface of the EXPERIMENTAL bundle tools. Thin proxy: the supervisor owns all logic and enforces the master switch.</summary>
public static class BundleToolsEndpoints
{
    private static IResult Data(Reply reply) => reply.Ok
        ? Results.Ok(reply.Lines is { Length: > 0 } ? JsonSerializer.Deserialize<JsonElement>(reply.Lines[0]) : JsonSerializer.SerializeToElement(new { ok = true }))
        : Results.Json(reply, statusCode: 409);

    public static void MapBundleTools(this WebApplication app)
    {
        app.MapGet("/api/bundle-tools", async (PipeClient pipe, CancellationToken ct) => Data(await pipe.Call(new("bundle-tools-status"), ct))).RequireAuthorization("bundles.read");
        app.MapPost("/api/bundle-tools/settings", async (JsonElement body, PipeClient pipe, CancellationToken ct) => Data(await pipe.Call(new("bundle-tools-settings", body.GetRawText()), ct))).RequireAuthorization("settings.write");
        app.MapGet("/api/bundle-tools/jobs/{id}", async (string id, PipeClient pipe, CancellationToken ct) => Data(await pipe.Call(new("bundle-tools-job", id), ct))).RequireAuthorization("bundles.read");
    }
}
