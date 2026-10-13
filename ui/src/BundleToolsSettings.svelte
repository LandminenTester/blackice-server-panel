<script lang="ts">
import {onMount} from 'svelte';
import type {Catalog} from './locales';
import Toggle from './components/Toggle.svelte';
import Experimental from './components/Experimental.svelte';
import JobLog from './components/JobLog.svelte';
let {t,request,onchanged=()=>{}}=$props<{t:Catalog;request:(url:string,body?:object)=>Promise<any>;onchanged?:()=>void}>();
type Settings={uploadEnabled:boolean;sourcesEnabled:boolean;restartAfterBuild:boolean;buildUi:boolean;keepBackups:number;maxUploadMb:number;commitName:string;commitEmail:string};
type Tool={id:string;installed:boolean;version:string|null;source:string;sizeMb:number;installable:boolean;note:string|null};
type Account={id:string;type:string;baseUrl:string;label:string;username:string;hasToken:boolean};
type State={enabled:boolean;settings:Settings;sourceFolder:string;toolchain:Tool[];accounts:Account[];job?:{id:string;state:string}|null};
let st=$state<State|null>(null),busy=$state(false),error=$state(''),saved=$state(false),keep=$state(3),max=$state(512),name=$state(''),email=$state('');
let account=$state({id:'',type:'github',baseUrl:'',label:'',username:'',token:''}),accountMessage=$state(''),accountOpen=$state(false);
let dialog=$state<HTMLDialogElement>()!,toolJob=$state(''),latest=$state<Record<string,string|null>>({}),checking=$state(false);
function adopt(s:State){st=s;keep=s.settings.keepBackups;max=s.settings.maxUploadMb;name=s.settings.commitName;email=s.settings.commitEmail;}
async function load(){try{adopt(await request('/api/bundle-tools'));}catch(e){error=(e as Error).message;}}
async function patch(body:object){busy=true;error='';saved=false;try{adopt(await request('/api/bundle-tools/settings',body));saved=true;onchanged();}catch(e){error=(e as Error).message;}finally{busy=false;}}
async function toolAction(kind:'install'|'remove',id:string){error='';try{const r=await request('/api/bundle-tools/tools/'+kind,{id});if(kind==='install')toolJob=r.id;else adopt(r);}catch(e){error=(e as Error).message;}}
async function checkLatest(){checking=true;error='';try{latest=await request('/api/bundle-tools/tools/latest');}catch(e){error=(e as Error).message;}finally{checking=false;}}
function toolDone(){toolJob='';load();}
async function saveAccount(e:SubmitEvent){e.preventDefault();error='';accountMessage='';try{await request('/api/bundle-tools/accounts/save',{id:account.id||null,type:account.type,baseUrl:account.baseUrl||null,label:account.label,username:account.username,token:account.token||null});account={id:'',type:'github',baseUrl:'',label:'',username:'',token:''};accountOpen=false;await load();}catch(err){error=(err as Error).message;}}
async function removeAccount(id:string){error='';try{await request('/api/bundle-tools/accounts/delete',{name:id});await load();}catch(err){error=(err as Error).message;}}
async function testAccount(id:string){error='';accountMessage='';try{const r=await request('/api/bundle-tools/accounts/test',{name:id});accountMessage=t.gaTestOk.replace('{user}',r.user||'?');}catch(err){error=(err as Error).message;}}
function editAccount(a:Account){account={id:a.id,type:a.type,baseUrl:a.baseUrl,label:a.label,username:a.username,token:''};accountOpen=true;}
function toggleMaster(value:boolean){if(value)dialog.showModal();else patch({enabled:false});}
onMount(load);
</script>
<section class="update-panel bundle-tools" aria-label={t.bundleToolsTitle}>
<div class="update-heading"><h2>{t.bundleToolsTitle} <Experimental label={t.experimental} hint={t.experimentalHint}/></h2></div>
<p class="muted">{t.bundleToolsIntro}</p>
<Toggle checked={st?.enabled??false} label={t.bundleToolsEnable} disabled={busy||!st} onchange={toggleMaster}/>
{#if st?.enabled}
<div class="bundle-tools-options">
<h3>{t.btFeatures}</h3>
<Toggle checked={st.settings.uploadEnabled} label={t.btUpload} disabled={busy} onchange={v=>patch({uploadEnabled:v})}/>
<Toggle checked={st.settings.sourcesEnabled} label={t.btSources} disabled={busy} onchange={v=>patch({sourcesEnabled:v})}/>
<h3>{t.btBuild}</h3>
<Toggle checked={st.settings.restartAfterBuild} label={t.btRestart} disabled={busy} onchange={v=>patch({restartAfterBuild:v})}/>
<Toggle checked={st.settings.buildUi} label={t.btBuildUi} disabled={busy} onchange={v=>patch({buildUi:v})}/>
<p class="muted">{t.btNoAutoUpdate}</p>
<form class="config-grid" onsubmit={(e)=>{e.preventDefault();patch({keepBackups:keep,maxUploadMb:max,commitName:name,commitEmail:email});}}>
<label>{t.btKeepBackups}<input type="number" min="1" max="20" bind:value={keep} disabled={busy}/></label>
<label>{t.btMaxUpload}<input type="number" min="1" max="2048" bind:value={max} disabled={busy}/></label>
<label>{t.btCommitName}<input bind:value={name} maxlength="100" disabled={busy}/></label>
<label>{t.btCommitEmail}<input type="email" bind:value={email} maxlength="150" disabled={busy}/></label>
<div class="actions"><button class="primary" disabled={busy}>{t.btSave}</button></div>
</form>
<p class="muted">{t.btLayout.replace('{folder}',st.sourceFolder)}</p>
<h3>{t.btTools}</h3>
<p class="muted">{t.btToolsHint}</p>
<ul class="tool-list">{#each st.toolchain as tool (tool.id)}<li>
<div><strong>{t['toolName_'+tool.id as keyof typeof t]}</strong> <span class={'job-badge '+(tool.installed?'succeeded':'')}>{tool.installed?t.toolInstalled+(tool.version?' '+tool.version:'')+(tool.source==='system'?' ('+t.toolSystem+')':''):t.toolMissing}</span>{#if latest[tool.id]}<small class="muted"> {t.toolLatest}: {latest[tool.id]}</small>{/if}<br/><small class="muted">{t.toolSize.replace('{mb}',String(tool.sizeMb))}{tool.note?' · '+tool.note:''}</small></div>
<div class="actions">{#if tool.installable}<button disabled={!!toolJob||busy} onclick={()=>toolAction('install',tool.id)}>{tool.installed?t.toolUpdate:t.toolInstall}</button>{/if}{#if tool.installed&&tool.source==='panel'}<button disabled={!!toolJob||busy} onclick={()=>toolAction('remove',tool.id==='wasi'?'wasi':tool.id)}>{t.toolRemove}</button>{/if}</div>
</li>{/each}</ul>
<div class="actions"><button disabled={checking||!!toolJob} onclick={checkLatest}>{checking?t.toolBusy:t.toolCheck}</button></div>
{#if toolJob}<JobLog {t} {request} jobId={toolJob} ondone={toolDone}/>{/if}
<h3>{t.gaTitle}</h3>
<p class="muted">{t.gaHint}</p>
{#if !st.accounts.length}<p class="muted">{t.gaNone}</p>{/if}
<ul class="tool-list">{#each st.accounts as a (a.id)}<li><div><strong>{a.label}</strong> <span class="job-badge">{t['gaType_'+a.type as keyof typeof t]}</span>{#if a.hasToken}<span class="job-badge succeeded">{t.gaHasToken}</span>{/if}<br/><small class="muted">{a.baseUrl}{a.username?' · '+a.username:''}</small></div><div class="actions"><button onclick={()=>testAccount(a.id)}>{t.gaTest}</button><button onclick={()=>editAccount(a)}>{t.btSave}…</button><button onclick={()=>removeAccount(a.id)}>{t.gaDelete}</button></div></li>{/each}</ul>
{#if accountMessage}<p class="notice" role="status">{accountMessage}</p>{/if}
{#if accountOpen}<form class="config-grid" onsubmit={saveAccount}>
<label>{t.gaType}<select bind:value={account.type} disabled={!!account.id}><option value="github">{t.gaType_github}</option><option value="gitlab">{t.gaType_gitlab}</option><option value="gitea">{t.gaType_gitea}</option></select></label>
<label>{t.gaBase}<input type="url" bind:value={account.baseUrl} placeholder={account.type==='github'?'https://github.com':account.type==='gitlab'?'https://gitlab.com':'https://codeberg.org'}/></label>
<label>{t.gaLabel}<input bind:value={account.label} maxlength="60"/></label>
<label>{t.gaUser}<input bind:value={account.username} maxlength="100" autocomplete="off"/></label>
<label>{t.gaToken}<input type="password" bind:value={account.token} autocomplete="new-password" required={!account.id} placeholder={account.id?t.gaTokenKeep:''}/></label>
<div class="actions"><button type="button" onclick={()=>{accountOpen=false;account={id:'',type:'github',baseUrl:'',label:'',username:'',token:''};}}>{t.cancel}</button><button class="primary">{t.gaSave}</button></div></form>
{:else}<div class="actions"><button onclick={()=>accountOpen=true}>{t.gaNew}</button></div>{/if}
</div>
{/if}
{#if saved}<p class="notice" role="status">{t.btSaved}</p>{/if}
{#if error}<p class="error" role="alert">{t.errors[error as keyof typeof t.errors]??t.errors['request.failed']}</p>{/if}
</section>
<dialog bind:this={dialog} aria-labelledby="bundle-tools-warning"><h2 id="bundle-tools-warning">{t.bundleToolsWarnTitle}</h2><p>{t.bundleToolsWarnBody}</p><div class="actions"><button onclick={()=>dialog.close()}>{t.cancel}</button><button class="primary" onclick={()=>{dialog.close();patch({enabled:true});}}>{t.bundleToolsEnable}</button></div></dialog>
