using System.Text.Json;
using System.IO.Compression;
using System.Formats.Tar;
using System.Security.Cryptography;
using System.Net.Http.Headers;
namespace ReplayPanel.Supervisor;
// Fetch OCI manifests and verify every compressed layer before extracting only the published seed.
public static class OciInstaller
{
    public static async Task ExtractSeed(string target, Action<long> progress, CancellationToken ct)
    {
        using var http=new HttpClient {Timeout=TimeSpan.FromMinutes(30)};
        var auth=await http.GetStringAsync("https://ghcr.io/token?service=ghcr.io&scope=repository:replayre/server:pull",ct);
        http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",JsonDocument.Parse(auth).RootElement.GetProperty("token").GetString());
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.oci.image.index.v1+json, application/vnd.oci.image.manifest.v1+json, application/vnd.docker.distribution.manifest.list.v2+json, application/vnd.docker.distribution.manifest.v2+json");
        var manifest=await Get(http,"latest",ct);
        if(manifest.TryGetProperty("manifests",out var manifests)) {
            var item=manifests.EnumerateArray().First(x=>x.TryGetProperty("platform",out var platform) && platform.GetProperty("os").GetString()=="linux" && platform.GetProperty("architecture").GetString()=="amd64");
            manifest=await Get(http,item.GetProperty("digest").GetString()!,ct);
        }
        foreach(var layer in manifest.GetProperty("layers").EnumerateArray()) {
            ct.ThrowIfCancellationRequested();
            var digest=layer.GetProperty("digest").GetString()!;
            if(!System.Text.RegularExpressions.Regex.IsMatch(digest,"^sha256:[a-f0-9]{64}$")) throw new InvalidOperationException("install.archive");
            var file=Path.Combine(target,".layer-"+Guid.NewGuid().ToString("N"));
            try {
                using var response=await http.GetAsync("https://ghcr.io/v2/replayre/server/blobs/"+digest,HttpCompletionOption.ResponseHeadersRead,ct);response.EnsureSuccessStatusCode();
                await using(var output=File.Create(file)) { await using var input=await response.Content.ReadAsStreamAsync(ct); var buffer=new byte[65536]; long bytes=0; int count;
                    while((count=await input.ReadAsync(buffer,ct))>0) { bytes+=count;if(bytes>4L*1024*1024*1024)throw new InvalidOperationException("install.size");await output.WriteAsync(buffer.AsMemory(0,count),ct);progress(bytes); } }
                await using(var verify=File.OpenRead(file)) if(Convert.ToHexString(await SHA256.HashDataAsync(verify,ct)).ToLowerInvariant()!=digest[7..]) throw new InvalidOperationException("install.integrity");
                await using var compressed=File.OpenRead(file); using var gzip=new GZipStream(compressed,CompressionMode.Decompress); using var tar=new TarReader(gzip);
                long expanded=0;
                while(await tar.GetNextEntryAsync(false,ct) is { } entry) {
                    expanded+=entry.Length;if(expanded>8L*1024*1024*1024)throw new InvalidOperationException("install.size");
                    var name=entry.Name.TrimStart('.','/');
                    const string prefix="opt/replay/seed/";
                    if(!name.StartsWith(prefix,StringComparison.Ordinal)) continue;
                    var relative=name[prefix.Length..]; if(relative.Length==0)continue;
                    var output=Path.GetFullPath(Path.Combine(target,relative));
                    if(!output.StartsWith(target+Path.DirectorySeparatorChar,StringComparison.Ordinal))throw new InvalidOperationException("install.archive");
                    if(entry.EntryType==TarEntryType.Directory) { Directory.CreateDirectory(output);continue; }
                    if(entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink)throw new InvalidOperationException("install.archive");
                    if(entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    if(Path.GetFileName(output).StartsWith(".wh.")) { var removed=Path.Combine(Path.GetDirectoryName(output)!,Path.GetFileName(output)[4..]);if(File.Exists(removed))File.Delete(removed); continue; }
                    await using(var destination=File.Create(output)) if(entry.DataStream is not null)await entry.DataStream.CopyToAsync(destination,ct);
                    if(!OperatingSystem.IsWindows())File.SetUnixFileMode(output,entry.Mode & (UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute|UnixFileMode.GroupRead|UnixFileMode.GroupExecute|UnixFileMode.OtherRead|UnixFileMode.OtherExecute));
                }
            } finally {File.Delete(file);}
        }
    }
    private static async Task<JsonElement> Get(HttpClient http,string reference,CancellationToken ct) =>
        JsonDocument.Parse(await http.GetStringAsync("https://ghcr.io/v2/replayre/server/manifests/"+reference,ct)).RootElement.Clone();
}
