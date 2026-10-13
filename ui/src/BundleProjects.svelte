<script lang="ts">
import {onMount} from 'svelte';
import type {Catalog,Locale} from './locales';
import Toggle from './components/Toggle.svelte';
import Select from './components/Select.svelte';
import Experimental from './components/Experimental.svelte';
import JobLog from './components/JobLog.svelte';
let {t,locale,request,tools,accounts,onChanged=()=>{}}=$props<{t:Catalog;locale:Locale;request:(url:string,body?:object)=>Promise<any>;tools:{id:string;installed:boolean}[];accounts:{id:string;type:string;label:string;hasToken:boolean}[];onChanged?:()=>void}>();
type Pkg={file:string;id:string;version:string;floating:boolean};
type Git={branch:string|null;commit:string|null;dirty:boolean;ahead:number;behind:number;incoming:string[];remote:string|null};
type Project={name:string;source:string;remote?:string|null;branch?:string|null;build?:{time:string;ok:boolean;error?:string|null}|null;hasSource:boolean;hasBuild:boolean;ui?:string|null;packages:Pkg[];hasBackup:boolean;git:Git|null};
let projects=$state<Project[]>([]),loading=$state(true),error=$state(''),jobId=$state(''),jobName=$state(''),busy=$state(false);
let createDialog=$state<HTMLDialogElement>()!,importDialog=$state<HTMLDialogElement>()!,removeDialog=$state<HTMLDialogElement>()!,removing=$state('');
let form=$state({name:'',author:'',description:'',withUi:false,framework:'svelte',typeScript:true,router:false,state:false,tailwind:false,build:true,repo:false,account:'',visibility:'private'});
let imp=$state({name:'',url:'',branch:'',account:'',build:false});
let pkg=$state<{name:string;current:Pkg[];versions:string[];latest:string|null;selected:string;pre:boolean;loading:boolean}|null>(null);
let refs=$state<{name:string;branches:string[];tags:string[];current:string;selected:string;loading:boolean}|null>(null);
let incoming=$state<{name:string;files:string[]}|null>(null);
let discard=$state<Record<string,boolean>>({});
let missing=$derived(tools.filter((x:{id:string;installed:boolean})=>!x.installed&&x.id==='dotnet').map((x:{id:string})=>t['toolName_'+x.id as keyof typeof t] as string));
let gitMissing=$derived(!tools.find((x:{id:string;installed:boolean})=>x.id==='git')?.installed);
let nameValid=$derived(/^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$/.test(form.name));
let importValid=$derived(/^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$/.test(imp.name)&&/^https?:\/\/\S+$/.test(imp.url));
const stateLib=$derived(form.framework==='vue'?'Pinia':form.framework==='react'?'Zustand':'Svelte stores');
const accountOptions=$derived([{value:'',label:t.bpAccountAuto},...accounts.filter((a:{hasToken:boolean})=>a.hasToken).map((a:{id:string;label:string;type:string})=>({value:a.id,label:a.label+' ('+a.type+')'}))]);
const repoAccounts=$derived(accounts.filter((a:{hasToken:boolean})=>a.hasToken).map((a:{id:string;label:string;type:string})=>({value:a.id,label:a.label+' ('+a.type+')'})));
const when=(v:string)=>new Date(v).toLocaleString(locale);
async function load(){loading=true;try{const r=await request('/api/bundle-tools/projects');projects=r.map((x:{project:Project;git:Git|null})=>({...x.project,git:x.git}));}catch(e){error=(e as Error).message;}finally{loading=false;}}
async function run(url:string,body:object,name:string){error='';busy=true;try{const job=await request(url,body);jobId=job.id;jobName=name;}catch(e){error=(e as Error).message;busy=false;}}
function done(state:string){busy=false;load();onChanged();}
async function create(e:SubmitEvent){
  e.preventDefault();createDialog.close();
  const ui=form.withUi?{framework:form.framework,typeScript:form.typeScript,router:form.router&&form.framework!=='svelte',state:form.state,tailwind:form.tailwind}:null;
  const repo=form.repo&&form.account?{accountId:form.account,visibility:form.visibility}:null;
  await run('/api/bundle-tools/projects/create',{name:form.name,author:form.author,description:form.description,build:form.build,ui,repo},form.name);
}
async function importRepo(e:SubmitEvent){e.preventDefault();importDialog.close();await run('/api/bundle-tools/projects/import',{name:imp.name,url:imp.url,branch:imp.branch||null,accountId:imp.account||null,build:imp.build},imp.name);}
async function check(p:Project){busy=true;error='';try{const g=await request('/api/bundle-tools/projects/check',{name:p.name});p.git=g;projects=[...projects];incoming={name:p.name,files:g.incoming};}catch(e){error=(e as Error).message;}finally{busy=false;}}
async function loadRefs(p:Project){refs={name:p.name,branches:[],tags:[],current:'',selected:'',loading:true};try{const r=await request('/api/bundle-tools/projects/refs',{name:p.name});refs={name:p.name,branches:r.branches,tags:r.tags,current:r.current,selected:'',loading:false};}catch(e){error=(e as Error).message;refs=null;}}
async function checkout(build:boolean){if(!refs||!refs.selected)return;const [kind,ref]=refs.selected.split(':');await run('/api/bundle-tools/projects/checkout',{name:refs.name,kind,ref,build},refs.name);refs=null;}
async function loadPackages(p:Project,pre=false){
  pkg={name:p.name,current:p.packages,versions:[],latest:null,selected:'',pre,loading:true};
  try{const r=await request('/api/bundle-tools/packages/info',{name:p.name,includePrerelease:pre});pkg={name:p.name,current:r.current,versions:r.versions,latest:r.latest,selected:r.latest??'',pre,loading:false};}
  catch(e){error=(e as Error).message;pkg=null;}
}
async function applyPackages(){
  if(!pkg||!pkg.selected)return;busy=true;error='';
  try{const r=await request('/api/bundle-tools/packages/set',{name:pkg.name,version:pkg.selected,includePrerelease:pkg.pre});pkg={...pkg,current:r.current};await load();}
  catch(e){error=(e as Error).message;}finally{busy=false;}
}
async function removeSource(){removeDialog.close();busy=true;try{const r=await request('/api/bundle-tools/projects/remove',{name:removing});projects=r.map((x:{project:Project;git:Git|null})=>({...(x.project??x),git:x.git??null}));onChanged();await load();}catch(e){error=(e as Error).message;}finally{busy=false;}}
onMount(load);
</script>
<section class="bundle-panel bundle-projects" aria-label={t.bpTitle}>
<div class="section-heading"><h2>{t.bpTitle} <Experimental label={t.experimental} hint={t.experimentalHint}/></h2><div class="actions"><button onclick={load} disabled={loading||busy}>{t.refresh}</button><button onclick={()=>importDialog.showModal()} disabled={busy}>{t.bpImport}</button><button class="primary" onclick={()=>createDialog.showModal()} disabled={busy}>{t.bpNew}</button></div></div>
<div class="bundle-upload-body">
<p class="muted">{t.bpIntro}</p>
{#if missing.length}<p class="error" role="status">{t.bpToolsMissing.replace('{tools}',missing.join(', '))}</p>{/if}
{#if !loading&&!projects.length}<p class="empty">{t.bpEmpty}</p>{/if}
<ul class="project-list">{#each projects as p (p.name)}<li>
<div class="project-head"><div><strong>{p.name}</strong> <span class="job-badge">{t['bpSource_'+p.source as keyof typeof t]??p.source}</span>{#if p.ui}<span class="job-badge">{t.bpUi}: {p.ui}</span>{/if}{#if p.git?.dirty}<span class="job-badge failed">{t.bpLocalChanges}</span>{/if}{#if p.git&&p.git.behind>0}<span class="job-badge">{t.bpBehind.replace('{n}',String(p.git.behind))}</span>{/if}</div>
<div class="actions"><button class="primary" disabled={busy||!p.hasSource} onclick={()=>run('/api/bundle-tools/projects/build',{name:p.name},p.name)}>{t.bpBuild}</button>{#if p.git}<button disabled={busy} onclick={()=>check(p)}>{t.bpCheck}</button><button disabled={busy} onclick={()=>run('/api/bundle-tools/projects/pull',{name:p.name,build:false,discard:!!discard[p.name]},p.name)}>{t.bpPull}</button><button disabled={busy} onclick={()=>run('/api/bundle-tools/projects/pull',{name:p.name,build:true,discard:!!discard[p.name]},p.name)}>{t.bpPullBuild}</button><button disabled={busy} onclick={()=>loadRefs(p)}>{t.bpVersion}</button>{/if}<button disabled={busy||!p.hasSource} onclick={()=>loadPackages(p)}>{t.bpPackages}</button>{#if p.hasBackup}<button disabled={busy} onclick={()=>run('/api/bundle-tools/projects/rollback',{name:p.name},p.name)}>{t.bpRollback}</button>{/if}<button disabled={busy} onclick={()=>{removing=p.name;removeDialog.showModal();}}>{t.bpRemove}</button></div></div>
{#if p.git}<p class="muted project-meta">{p.git.branch} @ {p.git.commit}{#if p.remote} · {p.remote}{/if}{#if p.git.dirty} · <label class="inline-check"><input type="checkbox" checked={!!discard[p.name]} onchange={(e)=>discard[p.name]=(e.currentTarget as HTMLInputElement).checked}/> {t.bpDiscard}</label>{/if}</p>{/if}
<p class="muted project-meta">{t.bpLastBuild}: {#if p.build}<span class={'job-badge '+(p.build.ok?'succeeded':'failed')}>{p.build.ok?t.bpOk:t.bpFailed}</span> {when(p.build.time)}{#if p.build.error} · {t.errors[p.build.error as keyof typeof t.errors]??p.build.error}{/if}{:else}{t.bpNever}{/if}{#if p.packages.length} · replay.blackice.* {[...new Set(p.packages.map(x=>x.version))].join(', ')}{/if}</p>
{#if incoming&&incoming.name===p.name}<div class="package-panel"><strong>{incoming.files.length?t.bpIncoming:t.bpUpToDate}</strong>{#if incoming.files.length}<pre class="job-lines">{incoming.files.join('\n')}</pre>{/if}</div>{/if}
{#if refs&&refs.name===p.name}<div class="package-panel">{#if refs.loading}<p class="muted">{t.loading}</p>{:else}
<div class="channel-row"><span>{t.bpCurrentRef}: <strong>{refs.current}</strong></span><Select value={refs.selected} label={t.bpVersion} options={[{value:'',label:t.bpChooseRef},...refs.branches.map(b=>({value:'branch:'+b,label:t.bpBranch+': '+b})),...refs.tags.map(x=>({value:'tag:'+x,label:t.bpTag+': '+x}))]} onchange={v=>{if(refs)refs.selected=v;}}/><button disabled={busy||!refs.selected} onclick={()=>checkout(false)}>{t.bpSwitch}</button><button disabled={busy||!refs.selected} onclick={()=>checkout(true)}>{t.bpSwitchBuild}</button></div>
<p class="muted">{t.bpNoAuto}</p>{/if}</div>{/if}
{#if pkg&&pkg.name===p.name}<div class="package-panel">
{#if !pkg.current.length}<p class="muted">{t.bpVersionNone}</p>{:else}
<ul class="checks">{#each pkg.current as c}<li class={c.floating?'warn':'ok'}><span aria-hidden="true">{c.floating?'!':'✓'}</span><span>{c.file}: {c.id} {c.version}{c.floating?' — '+t.bpFloating:''}</span></li>{/each}</ul>
<div class="channel-row"><Toggle checked={pkg.pre} label={t.bpIncludePre} disabled={pkg.loading} onchange={v=>loadPackages(p,v)}/></div>
{#if pkg.loading}<p class="muted">{t.loading}</p>{:else if pkg.versions.length}
<div class="channel-row"><span>{t.bpVersionNow}: <strong>{[...new Set(pkg.current.map(x=>x.version))].join(', ')}</strong> · {t.toolLatest}: <strong>{pkg.latest}</strong></span><Select value={pkg.selected} label={t.bpVersionApply} options={[...pkg.versions].reverse().map(v=>({value:v,label:v}))} onchange={v=>{if(pkg)pkg.selected=v;}}/><button disabled={busy||!pkg.selected} onclick={applyPackages}>{t.bpVersionApply}</button></div>{/if}
<p class="muted">{t.bpNoAuto}</p>{/if}
</div>{/if}
</li>{/each}</ul>
{#if jobId}<h3>{jobName}</h3><JobLog {t} {request} {jobId} ondone={done}/>{/if}
{#if error}<p class="error" role="alert">{t.errors[error as keyof typeof t.errors]??t.errors['request.failed']}</p>{/if}
</div>
</section>
<dialog bind:this={createDialog} aria-labelledby="bp-create-title" class="create-project"><form onsubmit={create}><h2 id="bp-create-title">{t.bpNew} <Experimental label={t.experimental} hint={t.experimentalHint}/></h2>
<div class="config-grid">
<label>{t.bpName}<input bind:value={form.name} maxlength="128" required aria-invalid={form.name!==''&&!nameValid}/></label>
<label>{t.bpAuthor}<input bind:value={form.author} maxlength="80"/></label>
<label>{t.bpDesc}<input bind:value={form.description} maxlength="300"/></label>
<Toggle checked={form.withUi} label={t.bpWithUi} onchange={v=>form.withUi=v}/>
{#if form.withUi}<div class="ui-options">
<div class="field"><span>{t.bpFramework}</span><Select value={form.framework} label={t.bpFramework} options={[{value:'svelte',label:'Svelte'},{value:'vue',label:'Vue'},{value:'react',label:'React'}]} onchange={v=>{form.framework=v;if(v==='svelte')form.router=false;}}/></div>
<Toggle checked={form.typeScript} label={t.bpTs} onchange={v=>form.typeScript=v}/>
{#if form.framework!=='svelte'}<Toggle checked={form.router} label={t.bpRouter+(form.framework==='vue'?' (Vue Router)':' (React Router)')} onchange={v=>form.router=v}/>{/if}
<Toggle checked={form.state} label={t.bpState.replace('{lib}',stateLib)} onchange={v=>form.state=v}/>
<Toggle checked={form.tailwind} label={t.bpTailwind} onchange={v=>form.tailwind=v}/>
</div>{/if}
{#if repoAccounts.length&&!gitMissing}<Toggle checked={form.repo} label={t.bpCreateRepo} onchange={v=>{form.repo=v;if(v&&!form.account)form.account=repoAccounts[0].value;}}/>
{#if form.repo}<div class="ui-options"><div class="field"><span>{t.bpAccount}</span><Select value={form.account} label={t.bpAccount} options={repoAccounts} onchange={v=>form.account=v}/></div><div class="field"><span>{t.bpVisibility}</span><Select value={form.visibility} label={t.bpVisibility} options={[{value:'private',label:t.bpPrivate},{value:'public',label:t.bpPublic}]} onchange={v=>form.visibility=v}/></div></div>{/if}
{:else}<p class="muted">{gitMissing?t.bpNoGit:t.bpNoAccount}</p>{/if}
<Toggle checked={form.build} label={t.bpBuildNow} onchange={v=>form.build=v}/>
<p class="muted">{t.bpBuildHint}</p>
</div>
<div class="actions"><button type="button" onclick={()=>createDialog.close()}>{t.cancel}</button><button class="primary" disabled={!nameValid}>{t.bpCreate}</button></div></form></dialog>
<dialog bind:this={importDialog} aria-labelledby="bp-import-title" class="create-project"><form onsubmit={importRepo}><h2 id="bp-import-title">{t.bpImport} <Experimental label={t.experimental} hint={t.experimentalHint}/></h2>
<div class="config-grid">
<label>{t.bpName}<input bind:value={imp.name} maxlength="128" required/></label>
<label>{t.bpRepoUrl}<input type="url" bind:value={imp.url} placeholder="https://github.com/owner/repo.git" required/></label>
<label>{t.bpBranchOptional}<input bind:value={imp.branch} maxlength="100" placeholder="main"/></label>
<div class="field"><span>{t.bpAccount}</span><Select value={imp.account} label={t.bpAccount} options={accountOptions} onchange={v=>imp.account=v}/></div>
{#if gitMissing}<p class="error">{t.bpNoGit}</p>{/if}
<Toggle checked={imp.build} label={t.bpBuildAfter} onchange={v=>imp.build=v}/>
<p class="muted">{t.bpImportHint}</p>
</div>
<div class="actions"><button type="button" onclick={()=>importDialog.close()}>{t.cancel}</button><button class="primary" disabled={!importValid||gitMissing}>{t.bpImportGo}</button></div></form></dialog>
<dialog bind:this={removeDialog} aria-labelledby="bp-remove-title"><h2 id="bp-remove-title">{t.bpRemove}</h2><p>{t.bpRemoveConfirm.replace('{name}',removing)}</p><div class="actions"><button onclick={()=>removeDialog.close()}>{t.cancel}</button><button class="danger" onclick={removeSource}>{t.bpRemove}</button></div></dialog>
