using System.IO.Compression;
namespace ReplayPanel.Supervisor;
public static class ArchiveInstaller
{
 public static void Extract(string zip,string staging,CancellationToken ct=default)
 {
  staging=Path.GetFullPath(staging);
  using var archive=ZipFile.OpenRead(zip); long expanded=0;
            foreach(var entry in archive.Entries) {
                ct.ThrowIfCancellationRequested(); expanded+=entry.Length; if(expanded>8L*1024*1024*1024) throw new InvalidOperationException("install.size");
                var relative=entry.FullName.StartsWith("reserver/",StringComparison.Ordinal)?entry.FullName[9..]:entry.FullName;
                if(relative.Length==0)continue;
                var target=Path.GetFullPath(Path.Combine(staging,relative));
                if(!target.StartsWith(staging+Path.DirectorySeparatorChar,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal) || (entry.ExternalAttributes >>16 & 0xF000)==0xA000) throw new InvalidOperationException("install.archive");
                if(entry.Name.Length==0) Directory.CreateDirectory(target); else { Directory.CreateDirectory(Path.GetDirectoryName(target)!); entry.ExtractToFile(target,false); }
            }
 }
}
