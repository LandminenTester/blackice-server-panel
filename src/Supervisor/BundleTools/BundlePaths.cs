namespace ReplayPanel.Supervisor.BundleTools;
/// <summary>Rejects links before copying or replacing bundle artifacts, including parent directories.</summary>
public static class BundlePaths{
 public static void Require(string path){
  for(var current=Path.GetFullPath(path);!string.IsNullOrEmpty(current);current=Path.GetDirectoryName(current)){
   try{if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("bundleTools.path");}catch(FileNotFoundException){}catch(DirectoryNotFoundException){}
  }
 }
 public static void Tree(string path){Require(path);if(!Directory.Exists(path))return;foreach(var entry in new DirectoryInfo(path).GetFileSystemInfos()){if((entry.Attributes&FileAttributes.ReparsePoint)!=0||entry.LinkTarget is not null)throw new InvalidOperationException("bundleTools.path");if(entry is DirectoryInfo)Tree(entry.FullName);}}
 public static void Delete(string path){
  var directory=new DirectoryInfo(path);if(directory.LinkTarget is not null||(directory.Attributes&FileAttributes.ReparsePoint)!=0){directory.Delete();return;}
  foreach(var entry in directory.GetFileSystemInfos()){
   if((entry.Attributes&FileAttributes.ReparsePoint)!=0){entry.Delete();continue;}
   if(entry is DirectoryInfo)Delete(entry.FullName);else{entry.Attributes=FileAttributes.Normal;entry.Delete();}
  }directory.Delete();
 }
}
