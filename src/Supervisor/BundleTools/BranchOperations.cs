using System.Text.Json;
using ReplayPanel.Shared;
namespace ReplayPanel.Supervisor.BundleTools;

public sealed record BranchSelection(string Name, string Branch, string Target, string? SourceCommit=null, string? TargetCommit=null, string? Title=null, bool Confirm=false);
public sealed record BranchComparison(string Branch,string Target,string SourceCommit,string TargetCommit,string[] Commits,string[] Files,bool Conflict);
public sealed partial class BundleProjects
{
    private (BundleProject Project,GitAccount Account,string Remote) RemoteProject(string name)
    {
        service.RequireSources();
        var p=Registry.Get(BundleLayout.RequireName(name))??throw new InvalidOperationException("bundleTools.noSource");
        var remote=GitService.ValidateRemote(p.Remote);
        return (p,AccountOf(p)??throw new InvalidOperationException("bundleTools.account"),remote);
    }
    private async Task<BranchComparison> CompareCore(BranchSelection args,CancellationToken ct)
    {
        var (p,a,remote)=RemoteProject(args.Name);GitService.ValidateRef(args.Branch);GitService.ValidateRef(args.Target);
        if(args.Branch==args.Target)throw new InvalidOperationException("bundleTools.sameBranch");
        var root=Path.Combine(BundleLayout.Stage(service.ConfigPath),"compare-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        async Task<string[]> Step(params string[] cmd){var r=await service.Git.RunAsync(cmd,root,a,remote,ct);if(r.ExitCode!=0)throw new InvalidOperationException("bundleTools.gitFailed");return r.Lines;}
        try{
            await Step("init","--quiet");await Step("remote","add","origin",remote);
            await Step("fetch","--quiet","--no-tags","origin","+refs/heads/"+args.Branch+":refs/remotes/origin/source","+refs/heads/"+args.Target+":refs/remotes/origin/target");
            var source=(await Step("rev-parse","origin/source"))[0].Trim();var target=(await Step("rev-parse","origin/target"))[0].Trim();
            var commits=await Step("log","--format=%h %s","-100","origin/target..origin/source");
            var files=await Step("diff","--name-status","origin/target...origin/source");
            var merge=await service.Git.RunAsync(["merge-tree","--write-tree","origin/target","origin/source"],root,null,"",ct);
            if(merge.ExitCode is not (0 or 1))throw new InvalidOperationException("bundleTools.gitFailed");
            return new(args.Branch,args.Target,source,target,commits,files.Take(200).ToArray(),merge.ExitCode==1);
        }finally{if(Directory.Exists(root))ForceDelete(root);}
    }
    public Task<BranchComparison> Compare(string json,CancellationToken ct)=>CompareCore(JsonSerializer.Deserialize<BranchSelection>(json,PanelConfig.Json)??throw new InvalidOperationException("request.invalid"),ct);
    public BundleJob Merge(string json,bool direct)
    {
        var args=JsonSerializer.Deserialize<BranchSelection>(json,PanelConfig.Json)??throw new InvalidOperationException("request.invalid");
        var (p,a,remote)=RemoteProject(args.Name);
        var policy=p.Workflow??Config.BundleTools.Workflow;
        if(direct?policy!="direct":policy=="branches")throw new InvalidOperationException("bundleTools.workflow");
        if(direct&&!args.Confirm)throw new InvalidOperationException("bundleTools.confirm");
        if(string.IsNullOrEmpty(args.SourceCommit)||string.IsNullOrEmpty(args.TargetCommit))throw new InvalidOperationException("bundleTools.branchChanged");
        var title=string.IsNullOrWhiteSpace(args.Title)?"Merge "+args.Branch+" → "+args.Target:args.Title;
        if(title.Length>200||title.Any(char.IsControl))throw new InvalidOperationException("bundleTools.invalid");
        return service.Jobs.Start(direct?"git-merge":"git-review",args.Name,async ctx=>{
            ctx.Phase("Checking remote branches");var check=await CompareCore(args,ctx.Token);
            if(check.SourceCommit!=args.SourceCommit||check.TargetCommit!=args.TargetCommit)throw new InvalidOperationException("bundleTools.branchChanged");
            if(check.Conflict)throw new InvalidOperationException("bundleTools.mergeConflict");
            if(check.Commits.Length==0)throw new InvalidOperationException("bundleTools.noChanges");
            var token=service.Secrets.Unprotect(a.TokenProtected);service.Redactor.Add(token);
            ctx.Phase(direct?"Merging remote branches":"Creating a review request");
            var result=await GitProviders.MergeRequestAsync(service.ProviderHttp,a,token,remote,args.Branch,args.Target,title,direct,check.SourceCommit,check.TargetCommit,ctx.Token,url=>{ctx.Result(url);ctx.Log("Review request: "+url);});
            ctx.Result(result);ctx.Log(result);
        });
    }
    private sealed record PackageReviewArgs(string Name,string Version,string Branch,bool IncludePrerelease=false);
    public BundleJob PackageReview(string json)
    {
        var args=JsonSerializer.Deserialize<PackageReviewArgs>(json,PanelConfig.Json)??throw new InvalidOperationException("request.invalid");
        var (p,a,remote)=RemoteProject(args.Name);GitService.ValidateRef(args.Branch);
        return service.Jobs.Start("package-review",args.Name,async ctx=>{
            var root=Path.Combine(BundleLayout.Stage(service.ConfigPath),ctx.Id);
            try{
                ctx.Phase("Preparing isolated package changes");await service.Git.CloneAsync(remote,args.Branch,root,a,ctx);
                BundlePaths.Tree(root);var current=NuGetService.Read(root);
                if(current.Length==0)throw new InvalidOperationException("bundleTools.noPackages");
                var versions=await nuget.CommonVersionsAsync(current.Select(c=>c.Id),args.IncludePrerelease,ctx.Token);
                if(!versions.Contains(args.Version,StringComparer.OrdinalIgnoreCase))throw new InvalidOperationException("bundleTools.package");
                var pins=current.Any(c=>c.Id.Equals("replay.blackice.server",StringComparison.OrdinalIgnoreCase))?await nuget.CompilerPinsAsync(args.Version,ctx.Token):[];
                if(NuGetService.Apply(root,args.Version,pins)==0)throw new InvalidOperationException("bundleTools.noChanges");
                var branch="panel/packages-"+ctx.Id;
                async Task Step(params string[] commands){var r=await service.Git.RunAsync(commands,root,a,remote,ctx.Token,ctx.Log);if(r.ExitCode!=0)throw new InvalidOperationException("bundleTools.gitFailed");}
                await Step("checkout","-b",branch);await Step("add","--","*.csproj");
                await Step("-c","user.name="+Config.BundleTools.CommitName,"-c","user.email="+Config.BundleTools.CommitEmail,"commit","-m","Update BlackICE packages to "+args.Version,"--quiet");
                ctx.Phase("Publishing package review branch");await Step("push","origin","HEAD:refs/heads/"+branch);
                var token=service.Secrets.Unprotect(a.TokenProtected);service.Redactor.Add(token);
                var url=await GitProviders.ReviewAsync(service.ProviderHttp,a,token,remote,branch,args.Branch,"Update BlackICE packages to "+args.Version,ctx.Token);ctx.Result(url);ctx.Log(url);
            }finally{if(Directory.Exists(root))ForceDelete(root);}
        });
    }
}
