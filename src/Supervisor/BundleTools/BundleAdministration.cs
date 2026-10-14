using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ReplayPanel.Shared;
namespace ReplayPanel.Supervisor.BundleTools;

public sealed record BundleLabels(string[] Tags,bool AutostartDisabled=false);
public sealed record BundleEdit(string Name,string[]? Tags=null,bool? AutostartDisabled=null,bool Sources=false,bool Backups=false,bool Data=false);
/// <summary>Panel metadata and explicit lifecycle operations. Never follows links or deletes external VFS mounts.</summary>
public sealed class BundleAdministration(PanelConfig config,string configPath,BundleToolsService tools)
{
    private readonly object gate=new();
    private string FilePath=>Path.Combine(BundleLayout.StateDir(configPath),"bundle-metadata.json");
    public Dictionary<string,BundleLabels> Read(){lock(gate){if(!File.Exists(FilePath))return new(StringComparer.Ordinal);return JsonSerializer.Deserialize<Dictionary<string,BundleLabels>>(File.ReadAllText(FilePath),PanelConfig.Json)??new();}}
    private void Save(Dictionary<string,BundleLabels> items){var temp=FilePath+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(items,PanelConfig.Json));File.Move(temp,FilePath,true);}
    public object Tags(string json){lock(gate){var input=JsonSerializer.Deserialize<BundleEdit>(json,PanelConfig.Json)??throw new InvalidOperationException("request.invalid");var name=BundleLayout.RequireName(input.Name);var tags=input.Tags??[];
        if(tags.Length>20||tags.Any(t=>string.IsNullOrWhiteSpace(t)||t.Length>40||t.Any(char.IsControl)))throw new InvalidOperationException("bundle.tagsInvalid");
        var items=Read();items[name]=(items.GetValueOrDefault(name)??new([])) with{Tags=tags.Select(t=>t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()};Save(items);return Read();}}
    public bool Disabled(string name)=>Read().GetValueOrDefault(name)?.AutostartDisabled==true;
    // A disabled start is encoded in-place: exact original text and ordering survive reenabling.
    private const string Prefix="# panel-autostart-disabled ";
    private Dictionary<string,string> ConfigFiles(){
        var files=new Dictionary<string,string>(OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal);
        void Visit(string file){file=Path.GetFullPath(file);if(!BundleLayout.IsPathInside(config.ServerRoot,file))throw new InvalidOperationException("bundle.autostartUnsupported");BundlePaths.Require(file);
            if(files.ContainsKey(file))return;if(files.Count>=50||!File.Exists(file)||new FileInfo(file).Length>1024*1024)throw new InvalidOperationException("bundle.autostartUnsupported");
            var text=File.ReadAllText(file);files.Add(file,text);
            foreach(var raw in text.Split('\n')){var line=raw.Trim();if(line.StartsWith('#')||line.StartsWith("//"))continue;
                if(Regex.IsMatch(line,@"^exec(?:\s|$)")){var m=Regex.Match(line,"^exec\\s+(?:\"([^\"]+)\"|([^\\s;]+))\\s*$");if(!m.Success)throw new InvalidOperationException("bundle.autostartUnsupported");var p=m.Groups[1].Success?m.Groups[1].Value:m.Groups[2].Value;if(Path.IsPathRooted(p)||p.Contains('$')||p.Contains('*'))throw new InvalidOperationException("bundle.autostartUnsupported");Visit(Path.Combine(config.ServerRoot,p));}
                if(Regex.IsMatch(line,@"^start(?:\s|$)")&&!Regex.IsMatch(line,@"^start\s+[A-Za-z0-9][A-Za-z0-9_.-]{0,127}\s*(?:(?:#|//).*)?$"))throw new InvalidOperationException("bundle.autostartUnsupported");
            }
        }
        Visit(Path.Combine(config.ServerRoot,"cfg","server.cfg"));return files;
    }
    private Dictionary<string,string> SetStart(string name,bool disabled){
        var files=ConfigFiles();var modified=new Dictionary<string,string>();var pattern=new Regex(@"^\s*start\s+"+Regex.Escape(name)+@"\s*(?:(?:#|//).*)?$");
        foreach(var (path,text) in files){var eol=text.Contains("\r\n")?"\r\n":"\n";var lines=text.Split(eol);var changed=false;
            for(var n=0;n<lines.Length;n++){
                if(disabled&&pattern.IsMatch(lines[n])){lines[n]=Prefix+name+" "+Convert.ToBase64String(Encoding.UTF8.GetBytes(lines[n]));changed=true;}
                else if(!disabled&&lines[n].StartsWith(Prefix+name+" ",StringComparison.Ordinal)){try{var original=Encoding.UTF8.GetString(Convert.FromBase64String(lines[n][(Prefix+name+" ").Length..]));if(!pattern.IsMatch(original))throw new FormatException();lines[n]=original;changed=true;}catch(FormatException){throw new InvalidOperationException("bundle.autostartUnsupported");}}
            }if(changed)modified[path]=string.Join(eol,lines);
        }
        var written=new List<string>();try{foreach(var (path,text) in modified){File.Copy(path,path+".panel-autostart.bak",true);File.WriteAllText(path+".panel.tmp",text,new UTF8Encoding(false));File.Move(path+".panel.tmp",path,true);written.Add(path);}}
        catch{foreach(var path in written)File.WriteAllText(path,files[path],new UTF8Encoding(false));throw;}
        return modified.Keys.ToDictionary(path=>path,path=>files[path]);
    }
    public object Autostart(string json){lock(gate){var input=JsonSerializer.Deserialize<BundleEdit>(json,PanelConfig.Json)??throw new InvalidOperationException("request.invalid");var name=BundleLayout.RequireName(input.Name);var disabled=input.AutostartDisabled??throw new InvalidOperationException("request.invalid");
        var items=Read();var previous=SetStart(name,disabled);items[name]=(items.GetValueOrDefault(name)??new([])) with{AutostartDisabled=disabled};try{Save(items);}catch{foreach(var (file,text) in previous)File.WriteAllText(file,text,new UTF8Encoding(false));throw;}return Read();}}
    public void BeforeStart(){lock(gate){foreach(var item in Read().Where(x=>x.Value.AutostartDisabled))SetStart(item.Key,true);}}
    public object DeleteInfo(string name){name=BundleLayout.RequireName(name);var target=BundleLayout.BundleDir(config,name);var vfs=Path.Combine(target,"vfs");var exclusive=Directory.Exists(vfs);try{BundlePaths.Tree(vfs);if(OperatingSystem.IsLinux())foreach(var line in File.ReadLines("/proc/self/mountinfo")){var fields=line.Split(' ');if(fields.Length<5){exclusive=false;break;}var mount=Regex.Replace(fields[4],@"\\([0-7]{3})",m=>((char)Convert.ToInt32(m.Groups[1].Value,8)).ToString());if(Path.GetFullPath(mount)==Path.GetFullPath(vfs)||BundleLayout.IsPathInside(vfs,mount))exclusive=false;}}catch(Exception e)when(e is InvalidOperationException or IOException or UnauthorizedAccessException){exclusive=false;}
        return new{dataAvailable=exclusive,hasSource=Directory.Exists(BundleLayout.SourceDir(config,name)),hasBackups=Directory.Exists(Path.Combine(BundleLayout.Backups(configPath),name))};}
    public BundleJob Delete(string json){var input=JsonSerializer.Deserialize<BundleEdit>(json,PanelConfig.Json)??throw new InvalidOperationException("request.invalid");var name=BundleLayout.RequireName(input.Name);
        return tools.Jobs.Start("bundle-delete",name,async ctx=>{
            var target=BundleLayout.BundleDir(config,name);BundlePaths.Require(target);
            if(Directory.Exists(target))foreach(var entry in new DirectoryInfo(target).GetFileSystemInfos())if(input.Data||entry.Name!="vfs"){BundlePaths.Require(entry.FullName);if(entry is DirectoryInfo)BundlePaths.Tree(entry.FullName);}
            if(input.Sources)BundlePaths.Tree(BundleLayout.SourceDir(config,name));
            if(input.Backups)BundlePaths.Tree(Path.Combine(BundleLayout.Backups(configPath),name));
            var manager=tools.Bundles??throw new InvalidOperationException("console.unavailable");var list=await manager.List(ctx.Token);
            var state=list.Bundles.FirstOrDefault(b=>b.Name==name)?.State;
            if(state=="running")await manager.Action(name,"stop",ctx.Token);else if(state!="stopped")throw new InvalidOperationException("bundle.changed");
            if(input.Data){var info=JsonSerializer.SerializeToElement(DeleteInfo(name));if(!info.GetProperty("dataAvailable").GetBoolean())throw new InvalidOperationException("bundle.dataUnsafe");}
            Autostart(JsonSerializer.Serialize(new BundleEdit(name,AutostartDisabled:true),PanelConfig.Json));
            ctx.Phase("Deleting selected bundle files");
            if(Directory.Exists(target))foreach(var entry in new DirectoryInfo(target).GetFileSystemInfos()){
                if(!input.Data&&entry.Name=="vfs")continue;if(entry is DirectoryInfo)BundlePaths.Delete(entry.FullName);else entry.Delete();
            }
            if(input.Sources){var source=BundleLayout.SourceDir(config,name);BundlePaths.Tree(source);if(Directory.Exists(source))BundlePaths.Delete(source);tools.Projects.Registry.Remove(name);}
            if(input.Backups){var backups=Path.Combine(BundleLayout.Backups(configPath),name);BundlePaths.Tree(backups);if(Directory.Exists(backups))BundlePaths.Delete(backups);}
            ctx.Result("Deleted "+name);
        });
    }
}
