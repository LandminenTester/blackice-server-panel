using ReplayPanel.Shared;
namespace ReplayPanel.Supervisor;
public static class ServerLocation {
 public static string? Issue(PanelConfig config) {
  if(!config.Configured)return null;
  if(!Directory.Exists(config.ServerRoot))return "serverPath.missing";
  // The official container seeds runtime files on its first start; the mount itself must exist.
  if(config.Backend!="docker"&&!File.Exists(Path.Combine(config.ServerRoot,config.Backend=="windows"?"REServer.exe":"REServer")))return "serverPath.files";
  return null;
 }
 public static string Validate(PanelConfig config,string path,ServerStatus status) {
  if(config.Backend=="docker")throw new InvalidOperationException("serverPath.docker");
  if(status.State is not "stopped")throw new InvalidOperationException("serverPath.running");
  SetupService.Validate(path,config.Backend);
  return Path.GetFullPath(path);
 }
}
