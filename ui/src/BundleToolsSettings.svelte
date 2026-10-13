<script lang="ts">
import {onMount} from 'svelte';
import type {Catalog} from './locales';
import Toggle from './components/Toggle.svelte';
import Experimental from './components/Experimental.svelte';
let {t,request,onchanged=()=>{}}=$props<{t:Catalog;request:(url:string,body?:object)=>Promise<any>;onchanged?:()=>void}>();
type Settings={uploadEnabled:boolean;sourcesEnabled:boolean;restartAfterBuild:boolean;buildUi:boolean;keepBackups:number;maxUploadMb:number;commitName:string;commitEmail:string};
type State={enabled:boolean;settings:Settings;sourceFolder:string};
let st=$state<State|null>(null),busy=$state(false),error=$state(''),saved=$state(false),keep=$state(3),max=$state(512),name=$state(''),email=$state('');
let dialog=$state<HTMLDialogElement>()!;
function adopt(s:State){st=s;keep=s.settings.keepBackups;max=s.settings.maxUploadMb;name=s.settings.commitName;email=s.settings.commitEmail;}
async function load(){try{adopt(await request('/api/bundle-tools'));}catch(e){error=(e as Error).message;}}
async function patch(body:object){busy=true;error='';saved=false;try{adopt(await request('/api/bundle-tools/settings',body));saved=true;onchanged();}catch(e){error=(e as Error).message;}finally{busy=false;}}
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
</div>
{/if}
{#if saved}<p class="notice" role="status">{t.btSaved}</p>{/if}
{#if error}<p class="error" role="alert">{t.errors[error as keyof typeof t.errors]??t.errors['request.failed']}</p>{/if}
</section>
<dialog bind:this={dialog} aria-labelledby="bundle-tools-warning"><h2 id="bundle-tools-warning">{t.bundleToolsWarnTitle}</h2><p>{t.bundleToolsWarnBody}</p><div class="actions"><button onclick={()=>dialog.close()}>{t.cancel}</button><button class="primary" onclick={()=>{dialog.close();patch({enabled:true});}}>{t.bundleToolsEnable}</button></div></dialog>
