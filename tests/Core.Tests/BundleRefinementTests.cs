using System.Net;
using System.Text.Json;
using ReplayPanel.Shared;
using ReplayPanel.Supervisor;
using ReplayPanel.Supervisor.BundleTools;
public static class BundleRefinementTests
{
    public static async Task Run(string root,Action<bool,string> check)
    {
        var home=Path.Combine(root,"bundle-refinement");Directory.CreateDirectory(Path.Combine(home,"cfg"));
        var path=Path.Combine(home,"panel.json");var c=new PanelConfig{ServerRoot=home};c.SetPassword("test-fixture-password");c.BundleTools.Enabled=true;c.BundleTools.SourcesEnabled=true;
        using var backend=new StoppedFixture();var manager=new BundleManager(new PipeClient(backend),c);using var tools=new BundleToolsService(c,path,()=>manager);
        var admin=new BundleAdministration(c,path,tools);var cfg=Path.Combine(home,"cfg","server.cfg");var extra=Path.Combine(home,"cfg","extra.cfg");
        var original="# retain order\r\nstart example # note\r\nexec cfg/extra.cfg\r\nstart other\r\n";File.WriteAllText(cfg,original);File.WriteAllText(extra,"start example\n");
        string Body(object value)=>JsonSerializer.Serialize(value,PanelConfig.Json);
        admin.Tags(Body(new BundleEdit("example",[" API ","Jobs","api"])));check(admin.Read()["example"].Tags.SequenceEqual(new[]{"API","Jobs"}),"bundle categories normalize and deduplicate without altering bundle files");
        admin.Autostart(Body(new BundleEdit("example",AutostartDisabled:true)));check(admin.Disabled("example")&&!File.ReadAllText(cfg).Contains("\nstart example")&&!File.ReadAllText(extra).StartsWith("start example"),"disable autostart updates main and static included configuration");
        check(File.Exists(cfg+".panel-autostart.bak")&&File.ReadAllText(cfg).Contains("start other"),"autostart edits back up configuration and preserve other bundles");
        admin.Autostart(Body(new BundleEdit("example",AutostartDisabled:false)));check(File.ReadAllText(cfg)==original&&File.ReadAllText(extra)=="start example\n","reenabling restores exact text, comments and line endings");
        admin.Autostart(Body(new BundleEdit("example",AutostartDisabled:true)));File.AppendAllText(cfg,"start example\r\n");admin.BeforeStart();check(!File.ReadAllText(cfg).Split('\n').Any(l=>l.TrimStart().StartsWith("start example")),"managed start reconciles externally added startup entries");
        File.AppendAllText(cfg,"exec $dynamic\r\n");try{admin.BeforeStart();throw new Exception("dynamic accepted");}catch(InvalidOperationException e){check(e.Message=="bundle.autostartUnsupported","dynamic configuration fails closed without claiming autostart control");}File.WriteAllText(cfg,original);
        var target=BundleLayout.BundleDir(c,"example");Directory.CreateDirectory(Path.Combine(target,"vfs"));File.WriteAllText(Path.Combine(target,"config.toml"),"[General]\n");File.WriteAllText(Path.Combine(target,"sv.wasm"),"artifact");File.WriteAllText(Path.Combine(target,"vfs","save"),"persistent");
        var source=BundleLayout.SourceDir(c,"example");Directory.CreateDirectory(source);File.WriteAllText(Path.Combine(source,"source.cs"),"retained");
        var job=admin.Delete(Body(new BundleEdit("example")));while(tools.Jobs.Busy)await Task.Delay(10);check(tools.Jobs.Current?.State=="succeeded","stopped bundle deletion is confirmed");check(!File.Exists(Path.Combine(target,"config.toml"))&&File.ReadAllText(Path.Combine(target,"vfs","save"))=="persistent"&&File.Exists(Path.Combine(source,"source.cs")),"default deletion preserves runtime data and project sources");
        check(Permissions.All.Contains("bundles.merge")&&Permissions.All.Contains("bundles.packages")&&Permissions.All.Contains("bundles.data.delete"),"new capabilities are independent explicit permissions");
        check(NuGetService.Compare("1.2.0-rc.10","1.2.0-rc.2")>0&&NuGetService.Compare("1.2.0-beta","1.2.0-rc.1")<0,"specific prerelease choices follow semantic version order");
        File.WriteAllText(Path.Combine(target,"config.toml"),"[General]\n");var backup=Path.Combine(BundleLayout.Backups(path),"example","one");Directory.CreateDirectory(backup);File.WriteAllText(Path.Combine(backup,"sv.wasm"),"backup");
        admin.Delete(Body(new BundleEdit("example",Sources:true,Backups:true,Data:true)));while(tools.Jobs.Busy)await Task.Delay(10);
        check(tools.Jobs.Current?.State=="succeeded"&&!Directory.Exists(source)&&!Directory.Exists(Path.GetDirectoryName(backup)!)&&!Directory.Exists(Path.Combine(target,"vfs")),"explicit expanded deletion removes only chosen sources, backups and local data");
        foreach(var provider in new[]{"github","gitlab","gitea"}){
            var account=new GitAccount{Type=provider,BaseUrl=provider=="github"?"https://github.com":"https://"+provider+".example"};
            using var handler=new MergeFixture(provider);using var http=new HttpClient(handler);
            var remote=account.BaseUrl+"/owner/repo.git";
            var url=await GitProviders.MergeRequestAsync(http,account,"test-secret",remote,"feature","dev","Merge feature",true,"source-sha","target-sha",CancellationToken.None);
            check(url.StartsWith(account.BaseUrl)&&handler.MergeCalls==1,provider+" direct merge confirms provider result exactly once");
            handler.Target="changed";try{await GitProviders.MergeRequestAsync(http,account,"test-secret",remote,"feature","dev","Merge feature",true,"source-sha","target-sha",CancellationToken.None);throw new Exception("changed target accepted");}catch(InvalidOperationException e){check(e.Message=="bundleTools.branchChanged"&&handler.MergeCalls==1,provider+" refuses changed target without retrying merge");}
            handler.Target="target-sha";handler.Confirmed=false;string? created=null;try{await GitProviders.MergeRequestAsync(http,account,"test-secret",remote,"feature","dev","Merge feature",true,"source-sha","target-sha",CancellationToken.None,url=>created=url);throw new Exception("unconfirmed accepted");}catch(InvalidOperationException e){check(e.Message=="bundleTools.mergeUnconfirmed"&&created is not null&&handler.MergeCalls==2,provider+" unconfirmed merge retains request link and is not retried");}
        }
        if(Environment.GetEnvironmentVariable("PANEL_TEST_BRANCH_NETWORK")=="1"){
            c.BundleTools.Accounts.Add(new GitAccount{Id="public-read",Type="github",BaseUrl="https://github.com"});
            tools.Projects.Registry.Update("remote-check",_=>{},()=>new(){Name="remote-check",Remote="https://github.com/LandminenTester/blackice-server-panel.git",AccountId="public-read"});
            var comparison=await tools.Projects.Compare(JsonSerializer.Serialize(new BranchSelection("remote-check","experimental","main"),PanelConfig.Json),CancellationToken.None);
            check(comparison.SourceCommit.Length==40&&comparison.TargetCommit.Length==40,"real remote branch comparison reads exact commits without checkout or remote writes");
        }
        foreach(var framework in new[]{"react","vue","svelte"})foreach(var state in framework=="react"?new[]{"none","zustand","jotai","nanostores"}:framework=="vue"?new[]{"none","pinia","nanostores"}:new[]{"none","runes","nanostores"}){
            var files=BundleTemplates.Render(new NewProject("state-test","a","d","1.1.0",new(framework,true,false,state!="none",false,StateLibrary:state)));
            check(files.ContainsKey("ui/package.json"),framework+" "+state+" state template renders");
            if(Environment.GetEnvironmentVariable("PANEL_TEST_STATE_TEMPLATES")=="1"){
                var app=Path.Combine(home,"templates",framework+"-"+state);foreach(var (file,text) in files.Where(f=>f.Key.StartsWith("ui/"))){var output=Path.Combine(app,file[3..]);Directory.CreateDirectory(Path.GetDirectoryName(output)!);File.WriteAllText(output,text);}
                using var client=new HttpClient();var toolchain=new Toolchain(Path.Combine(home,"template-tools"),client);
                foreach(var args in new[]{new[]{"install","--ignore-scripts","--no-audit","--no-fund"},new[]{"run","check"},new[]{"run","build"}}){var lines=new List<string>();var result=await ProcessRunner.Run(toolchain.NodeExe!,[toolchain.NpmCli!,..args],app,toolchain.BuildEnvironment(),l=>lines.Add(l),CancellationToken.None,TimeSpan.FromMinutes(4));if(result.ExitCode!=0)Console.WriteLine(string.Join('\n',lines.TakeLast(16)));check(result.ExitCode==0&&!result.TimedOut,framework+" "+state+" "+string.Join(' ',args));}
            }
        }
    }
    private sealed class StoppedFixture:IServerBackend,IDisposable{
        public ServerStatus Status()=>new("stopped",null,"","keep-running");public Task StartAsync()=>Task.CompletedTask;public Task StopAsync(bool force=false)=>Task.CompletedTask;public Task SendAsync(string command)=>Task.CompletedTask;public string[] ConsoleSnapshot()=>[];public string[] LogSnapshot()=>[];public ValueTask DisposeAsync()=>ValueTask.CompletedTask;public void Dispose(){}
    }
    private sealed class MergeFixture(string provider):HttpMessageHandler{
        public int MergeCalls;public string Target="target-sha";public bool Confirmed=true;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){
            var path=request.RequestUri!.AbsolutePath;string body;
            if(path.EndsWith("/merge")){MergeCalls++;body=provider=="gitlab"?JsonSerializer.Serialize(new{state=Confirmed?"merged":"opened"}):JsonSerializer.Serialize(new{merged=Confirmed});}
            else if(path.Contains("/branches/"))body=JsonSerializer.Serialize(new{commit=new Dictionary<string,string>{{provider=="gitlab"?"id":"sha",Target}}});
            else body=JsonSerializer.Serialize(new{iid=1,number=1,merged=Confirmed,html_url="https://"+(provider=="github"?"github.com":provider+".example")+"/owner/repo/pulls/1",web_url="https://"+provider+".example/owner/repo/merge_requests/1"});
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body)});
        }
    }
}
