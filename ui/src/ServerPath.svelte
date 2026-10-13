<script lang="ts">
import type {Catalog} from './locales';
let {t,request,serverRoot,serverState,backend,connected,onchanged}=$props<{t:Catalog;request:(url:string,body?:object)=>Promise<any>;serverRoot:string;serverState:string;backend:string;connected:boolean;onchanged:(status:any)=>void}>();
let path=$state(''),dirty=$state(false),busy=$state(false),error=$state(''),saved=$state(false);
$effect(()=>{if(!dirty)path=serverRoot;});
let editable=$derived(connected&&serverState==='stopped'&&backend!=='docker');
async function save(e:SubmitEvent){e.preventDefault();busy=true;error='';saved=false;try{const reply=await request('/api/server-path',{serverRoot:path});onchanged(reply.status);dirty=false;saved=true;}catch(e){error=(e as Error).message;}finally{busy=false;}}
</script>
<section class="update-panel general-panel server-path-panel">
<h2>{t.serverRoot}</h2>
<form onsubmit={save}><label class="sr-only" for="server-path">{t.directPath}</label><div class="server-path-row"><input id="server-path" bind:value={path} oninput={()=>{dirty=true;error='';saved=false;}} disabled={!editable||busy} required maxlength="4096" spellcheck="false" autocomplete="off"/>{#if backend!=='docker'}<button type="submit" disabled={!editable||busy||!dirty||!path.trim()}>{busy?t.actionPending:t.pathSave}</button>{/if}</div></form>
<p class="muted">{backend==='docker'?t.pathDockerHint:editable?t.pathHint:t.pathStopHint}</p>
{#if saved}<p class="notice" role="status">{t.pathSaved}</p>{/if}
{#if error}<p class="error" role="alert">{t.errors[error as keyof typeof t.errors]??t.errors['request.failed']}</p>{/if}
</section>
