using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ReplayPanel.Shared;
namespace ReplayPanel.Supervisor.BundleTools;

public sealed record RepoInfo(string CloneUrl, string WebUrl, string DefaultBranch);

/// <summary>Creates repositories on GitHub, GitLab and Gitea/Forgejo/Codeberg with a personal access token. The token only ever travels in an Authorization header.</summary>
public static class GitProviders
{
    public static readonly string[] Types = ["github", "gitlab", "gitea"];

    public static string NormalizeBase(string type, string baseUrl)
    {
        if (!Uri.TryCreate((baseUrl ?? "").Trim().TrimEnd('/'), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0) throw new InvalidOperationException("bundleTools.account");
        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    public static string DefaultBase(string type) => type switch { "github" => "https://github.com", "gitlab" => "https://gitlab.com", _ => "https://codeberg.org" };

    public static string ApiBase(GitAccount account)
    {
        var baseUrl = account.BaseUrl.TrimEnd('/');
        return account.Type switch
        {
            "github" => new Uri(baseUrl).Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ? "https://api.github.com" : baseUrl + "/api/v3",
            "gitlab" => baseUrl + "/api/v4",
            _ => baseUrl + "/api/v1",
        };
    }

    private static HttpRequestMessage Request(HttpMethod method, string url, GitAccount account, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.UserAgent.ParseAdd("BlackICE-Panel");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        switch (account.Type)
        {
            case "github": request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28"); break;
            case "gitlab": request.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", token); break;
            default: request.Headers.Authorization = new AuthenticationHeaderValue("token", token); break;
        }
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return request;
    }

    public static async Task<string> TestAsync(HttpClient http, GitAccount account, string token, CancellationToken ct)
    {
        using var response = await http.SendAsync(Request(HttpMethod.Get, ApiBase(account) + "/user", account, token), ct);
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden) throw new InvalidOperationException("bundleTools.tokenRejected");
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("bundleTools.providerFailed");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var key = account.Type == "gitlab" ? "username" : "login";
        return doc.RootElement.TryGetProperty(key, out var name) ? name.GetString() ?? "" : "";
    }

    public static async Task<RepoInfo> CreateRepositoryAsync(HttpClient http, GitAccount account, string token, string name, string description, bool isPrivate, CancellationToken ct)
    {
        var api = ApiBase(account);
        object body = account.Type switch
        {
            "github" => new { name, description, @private = isPrivate, auto_init = false },
            "gitlab" => new { name, path = name, description, visibility = isPrivate ? "private" : "public", initialize_with_readme = false },
            _ => new { name, description, @private = isPrivate, auto_init = false, default_branch = "main" },
        };
        var url = account.Type == "gitlab" ? api + "/projects" : api + "/user/repos";
        using var response = await http.SendAsync(Request(HttpMethod.Post, url, account, token, body), ct);
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden) throw new InvalidOperationException("bundleTools.tokenRejected");
        if (response.StatusCode is System.Net.HttpStatusCode.Conflict or System.Net.HttpStatusCode.UnprocessableEntity or System.Net.HttpStatusCode.BadRequest) throw new InvalidOperationException("bundleTools.repoExists");
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("bundleTools.providerFailed");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var clone = (account.Type == "gitlab" ? root.GetProperty("http_url_to_repo") : root.GetProperty("clone_url")).GetString()!;
        var web = (account.Type == "gitlab" ? root.GetProperty("web_url") : root.GetProperty("html_url")).GetString()!;
        var branch = root.TryGetProperty("default_branch", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString()! : "main";
        // The repository must live on the account's own host; refuse anything else a response might point to.
        var expected = new Uri(account.BaseUrl).Host;
        if (!Uri.TryCreate(clone, UriKind.Absolute, out var cloneUri) || !cloneUri.Host.Equals(expected, StringComparison.OrdinalIgnoreCase) || cloneUri.UserInfo.Length > 0) throw new InvalidOperationException("bundleTools.providerFailed");
        return new RepoInfo(clone, web, branch.Length > 0 ? branch : "main");
    }
}
