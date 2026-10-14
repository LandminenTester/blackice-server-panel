using System.Diagnostics;
using System.Text.RegularExpressions;
namespace ReplayPanel.Supervisor.BundleTools;

// Discovery uses host installation locations, never a source project's working directory.
internal static class SystemTools
{
 internal static string RealPath(string path){try{return File.ResolveLinkTarget(path,true)?.FullName??path;}catch(IOException){return path;}}
 internal static string? SdkVersion(string root){
  try{return Directory.Exists(Path.Combine(root,"sdk"))?Directory.EnumerateDirectories(Path.Combine(root,"sdk")).Select(Path.GetFileName).Where(x=>x is not null&&Regex.IsMatch(x,@"^10\.[0-9]+\.[0-9]+(?:-.*)?$")).OrderByDescending(x=>Version.Parse(x!.Split('-')[0])).FirstOrDefault():null;}
  catch(Exception e)when(e is IOException or UnauthorizedAccessException){return null;}
 }
 internal static IEnumerable<string> Candidates(string name){
  var win=OperatingSystem.IsWindows();var exe=name+(win?".exe":"");
  var paths=new List<string?>();
  if(name=="dotnet")foreach(var variable in new[]{"DOTNET_ROOT_X64","DOTNET_ROOT"}){var value=Environment.GetEnvironmentVariable(variable);if(!string.IsNullOrEmpty(value))paths.Add(Path.Combine(value,exe));}
  var path=Environment.GetEnvironmentVariable("PATH")??"";
  if(win)foreach(var target in new[]{EnvironmentVariableTarget.Machine,EnvironmentVariableTarget.User})path+=Path.PathSeparator+Environment.GetEnvironmentVariable("PATH",target);
  foreach(var folder in path.Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries)){
   var expanded=Environment.ExpandEnvironmentVariables(folder.Trim().Trim('"'));
   if(Path.IsPathFullyQualified(expanded))paths.Add(Path.Combine(expanded,exe));
  }
  var home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
  if(win){
   foreach(var variable in new[]{"ProgramW6432","ProgramFiles","LOCALAPPDATA"}){
    var location=Environment.GetEnvironmentVariable(variable);if(string.IsNullOrWhiteSpace(location))continue;
    paths.Add(Path.Combine(location,name=="git"?"Git/cmd":name=="node"?"nodejs":"dotnet",exe));
    if(name=="git")paths.Add(Path.Combine(location,"Programs/Git/cmd",exe));
   }
  }else foreach(var folder in new[]{"/usr/bin","/usr/local/bin","/bin",name=="dotnet"?"/usr/share/dotnet":"/opt/node/bin"})paths.Add(Path.Combine(folder,exe));
  if(name=="dotnet")paths.Add(Path.Combine(home,".dotnet",exe));
  return paths.OfType<string>().Where(File.Exists).Distinct(OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal);
 }
 internal static string? Probe(string exe,string argument){
  try{
   using var process=Process.Start(new ProcessStartInfo(exe,argument){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true});
   if(process is null)return null;
   var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
   if(!process.WaitForExit(3000)){try{process.Kill(true);}catch(InvalidOperationException){}return null;}
   if(process.ExitCode!=0)return null;
   return output.GetAwaiter().GetResult().Trim();
  }catch(Exception e)when(e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException){return null;}
 }
 internal static string? Npm(string node){
  var folder=Path.GetDirectoryName(RealPath(node))!;
  return new[]{Path.Combine(folder,"node_modules/npm/bin/npm-cli.js"),Path.Combine(folder,"../lib/node_modules/npm/bin/npm-cli.js"),Path.Combine(folder,"../share/nodejs/npm/bin/npm-cli.js")}.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
 }
 internal static string? WasiVersion(string path){try{return File.ReadLines(Path.Combine(path,"VERSION")).FirstOrDefault()?.Trim();}catch(Exception e)when(e is IOException or UnauthorizedAccessException){return null;}}
 internal static bool Wasi(string path)=>File.Exists(Path.Combine(path,"VERSION"))&&File.Exists(Path.Combine(path,"bin",OperatingSystem.IsWindows()?"clang.exe":"clang"));
 internal static string? WasiPath(){
  foreach(var variable in new[]{"WASI_SDK_PATH","WasiSdkRoot"}){var value=Environment.GetEnvironmentVariable(variable);if(!string.IsNullOrEmpty(value)&&Path.IsPathFullyQualified(value)&&Wasi(value))return value;}
  var cache=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".wasi-sdk");
  try{return Directory.Exists(cache)?Directory.EnumerateDirectories(cache,"wasi-sdk-*").OrderDescending().FirstOrDefault(Wasi):null;}catch(Exception e)when(e is IOException or UnauthorizedAccessException){return null;}
 }
}
