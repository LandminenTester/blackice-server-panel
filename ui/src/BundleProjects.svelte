<script lang="ts">
import {onMount} from 'svelte';
import type {Catalog,Locale} from './locales';
import Toggle from './components/Toggle.svelte';
import Select from './components/Select.svelte';
import Experimental from './components/Experimental.svelte';
import JobLog from './components/JobLog.svelte';
let {t,locale,request,tools,onChanged=()=>{}}=$props<{t:Catalog;locale:Locale;request:(url:string,body?:object)=>Promise<any>;tools:{id:string;installed:boolean}[];onChanged?:()=>void}>();
type Pkg={file:string;id:string;version:string;floating:boolean};
type Project={name:string;source:string;remote?:string|null;branch?:string|null;build?:{time:string;ok:boolean;error?:string|null}|null;hasSource:boolean;hasBuild:boolean;ui?:string|null;packages:Pkg[];hasBackup:boolean};
let projects=$state<Project[]>([]),loading=$state(true),error=$state(''),jobId=$state(''),jobName=$state(''),busy=$state(false);
let createDialog=$state<HTMLDialogElement>()!,removeDialog=$state<HTMLDialogElement>()!,removing=$state('');
let form=$state({name:'',author:'',description:'',withUi:false,framework:'svelte',typeScript:true,router:false,state:false,tailwind:false,build:true});
let pkg=$state<{name:string;current:Pkg[];versions:string[];latest:string|null;selected:string;pre:boolean;loading:boolean}|null>(null);
let missing=$derived(tools.filter((x:{id:string;installed:boolean})=>!x.installed&&x.id!=='wasi'&&(x.id==='dotnet'||(x.id==='git'&&false))).map((x:{id:string})=>t['toolName_'+x.id as keyof typeof t] as string));
let nameValid=$derived(/^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$/.test(form.name));
const stateLib=$derived(form.framework==='vue'?'Pinia':form.framework==='react'?'Zustand':'Svelte stores');
const when=(v:string)=>new Date(v).toLocaleString(locale);
async function load(){loading=true;try{projects=await request('/api/bundle-tools/projects');}catch(e){error=(e as Error).message;}finally{loading=false;}}
async function run(url:string,body:object,name:string){error='';busy=true;try{const job=await request(url,body);jobId=job.id;jobName=name;}catch(e){error=(e as Error).message;busy=false;}}
function done(state:string){busy=false;load();onChanged();}
async function create(e:SubmitEvent){
  e.preventDefault();createDialog.close();
  const ui=form.withUi?{framework:form.framework,typeScript:form.typeScript,router:form.router&&form.framework!=='svelte',state:form.state,tailwind:form.tailwind}:null;
  await run('/api/bundle-tools/projects/create',{name:form.name,author:form.author,description:form.description,build:form.build,ui},form.name);
}
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
async function removeSource(){removeDialog.close();busy=true;try{projects=await request('/api/bundle-tools/projects/remove',{name:removing});onChanged();}catch(e){error=(e as Error).message;}finally{busy=false;}}
onMount(load);
</script>
<section class="bundle-panel bundle-projects" aria-label={t.bpTitle}>
<div class="section-heading"><h2>{t.bpTitle} <Experimental label={t.experimental} hint={t.experimentalHint}/></h2><div class="actions"><button onclick={load} disabled={loading||busy}>{t.refresh}</button><button class="primary" onclick={()=>createDialog.showModal()} disabled={busy}>{t.bpNew}</button></div></div>
<div class="bundle-upload-body">
<p class="muted">{t.bpIntro}</p>
{#if missing.length}<p class="error" role="status">{t.bpToolsMissing.replace('{tools}',missing.join(', '))}</p>{/if}
{#if !loading&&!projects.length}<p class="empty">{t.bpEmpty}</p>{/if}
<ul class="project-list">{#each projects as p (p.name)}<li>
<div class="project-head"><div><strong>{p.name}</strong> <span class="job-badge">{t['bpSource_'+p.source as keyof typeof t]??p.source}</span>{#if p.ui}<span class="job-badge">{t.bpUi}: {p.ui}</span>{/if}</div>
<div class="actions"><button class="primary" disabled={busy||!p.hasSource} onclick={()=>run('/api/bundle-tools/projects/build',{name:p.name},p.name)}>{t.bpBuild}</button><button disabled={busy||!p.hasSource} onclick={()=>loadPackages(p)}>{t.bpPackages}</button>{#if p.hasBackup}<button disabled={busy} onclick={()=>run('/api/bundle-tools/projects/rollback',{name:p.name},p.name)}>{t.bpRollback}</button>{/if}<button disabled={busy} onclick={()=>{removing=p.name;removeDialog.showModal();}}>{t.bpRemove}</button></div></div>
<p class="muted project-meta">{t.bpLastBuild}: {#if p.build}<span class={'job-badge '+(p.build.ok?'succeeded':'failed')}>{p.build.ok?t.bpOk:t.bpFailed}</span> {when(p.build.time)}{#if p.build.error} · {t.errors[p.build.error as keyof typeof t.errors]??p.build.error}{/if}{:else}{t.bpNever}{/if}{#if p.packages.length} · replay.blackice.* {[...new Set(p.packages.map(x=>x.version))].join(', ')}{/if}</p>
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
<Toggle checked={form.build} label={t.bpBuildNow} onchange={v=>form.build=v}/>
<p class="muted">{t.bpBuildHint}</p>
</div>
<div class="actions"><button type="button" onclick={()=>createDialog.close()}>{t.cancel}</button><button class="primary" disabled={!nameValid}>{t.bpCreate}</button></div></form></dialog>
<dialog bind:this={removeDialog} aria-labelledby="bp-remove-title"><h2 id="bp-remove-title">{t.bpRemove}</h2><p>{t.bpRemoveConfirm.replace('{name}',removing)}</p><div class="actions"><button onclick={()=>removeDialog.close()}>{t.cancel}</button><button class="danger" onclick={removeSource}>{t.bpRemove}</button></div></dialog>
