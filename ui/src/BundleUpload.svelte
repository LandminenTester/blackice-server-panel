<script lang="ts">
import type {Catalog,Locale} from './locales';
import Experimental from './components/Experimental.svelte';
import Toggle from './components/Toggle.svelte';
import JobLog from './components/JobLog.svelte';
let {t,locale,request,upload,maxMb,onInstalled=()=>{}}=$props<{t:Catalog;locale:Locale;request:(url:string,body?:object)=>Promise<any>;upload:(file:File,progress:(percent:number)=>void)=>Promise<any>;maxMb:number;onInstalled?:()=>void}>();
type Check={id:string;status:string;detail?:string};
type Report={ok:boolean;name:string;root:string;size:number;files:number;meta:{author?:string;description?:string;version?:string;serverBlob?:string;clientBlob?:string;hasUi:boolean;hasVfs:boolean}|null;checks:Check[]};
let drag=$state(false),busy=$state(false),percent=$state(0),error=$state(''),file=$state<{file:string;original:string;report:Report;exists:boolean}|null>(null),name=$state(''),replace=$state(false),jobId=$state(''),finished=$state(false);
const size=(n:number)=>n<1048576?(n/1024).toFixed(1)+' KB':(n/1048576).toFixed(1)+' MB';
const icon=(s:string)=>s==='ok'?'✓':s==='fail'?'✕':'!';
async function choose(selected:File|undefined|null){
  if(!selected)return;error='';file=null;finished=false;jobId='';busy=true;percent=0;
  if(!selected.name.toLowerCase().endsWith('.zip')){error='bundleTools.zipInvalid';busy=false;return;}
  if(selected.size>maxMb*1048576){error='bundleTools.uploadSize';busy=false;return;}
  try{const r=await upload(selected,(p:number)=>percent=p);file={file:r.file,original:selected.name,report:r.report,exists:r.exists};name=r.report.name;replace=false;}
  catch(e){error=(e as Error).message;}finally{busy=false;}
}
async function install(){
  if(!file)return;busy=true;error='';
  try{const job=await request('/api/bundle-tools/upload/install',{file:file.file,originalName:file.original,name,replace});jobId=job.id;}
  catch(e){error=(e as Error).message;busy=false;}
}
function done(state:string){busy=false;if(state==='succeeded'){finished=true;file=null;onInstalled();}}
let nameValid=$derived(/^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$/.test(name));
</script>
<section class="bundle-panel bundle-upload" aria-label={t.buUploadTitle}>
<div class="section-heading"><h2>{t.buUploadTitle} <Experimental label={t.experimental} hint={t.experimentalHint}/></h2></div>
<div class="bundle-upload-body">
<p class="muted">{t.buUploadHint.replace('{mb}',String(maxMb))}</p>
{#if !file&&!jobId}
<label class="dropzone" class:drag ondragover={(e)=>{e.preventDefault();drag=true;}} ondragleave={()=>drag=false} ondrop={(e)=>{e.preventDefault();drag=false;choose(e.dataTransfer?.files?.[0]);}}>
<input type="file" accept=".zip,application/zip" disabled={busy} onchange={(e)=>choose((e.currentTarget as HTMLInputElement).files?.[0])}/>
<strong>{busy?t.buUploading+' '+percent+'%':t.buDrop}</strong><small>{t.buChoose}</small>
</label>
{#if busy}<progress max="100" value={percent} aria-label={t.buUploading}></progress>{/if}
{/if}
{#if file}
<div class="upload-report">
<h3>{file.original} <small>{size(file.report.size)} · {file.report.files} {t.buFiles}</small></h3>
{#if file.report.meta}<dl class="upload-meta"><dt>{t.buAuthor}</dt><dd>{file.report.meta.author??'—'}</dd><dt>{t.buVersion}</dt><dd>{file.report.meta.version??'—'}</dd><dt>{t.buDescription}</dt><dd>{file.report.meta.description??'—'}</dd></dl>{/if}
<ul class="checks" aria-label={t.buChecks}>{#each file.report.checks as c}<li class={c.status}><span aria-hidden="true">{icon(c.status)}</span><span>{c.detail??c.id}</span></li>{/each}</ul>
{#if file.report.ok}
<p class="muted">{t.buStructureOnly}</p>
<label>{t.buName}<input bind:value={name} maxlength="128" aria-invalid={!nameValid} disabled={busy}/></label>
{#if file.exists}<div class="field"><p class="notice-inline">{t.buExists}</p><Toggle checked={replace} label={t.buReplace} onchange={v=>replace=v}/></div>{/if}
<div class="actions"><button onclick={()=>{file=null;}} disabled={busy}>{t.cancel}</button><button class="primary" disabled={busy||!nameValid||(file.exists&&!replace)} onclick={install}>{t.buInstall}</button></div>
{:else}<div class="actions"><button onclick={()=>{file=null;}}>{t.buTryAgain}</button></div>{/if}
</div>
{/if}
{#if jobId}<JobLog {t} {request} {jobId} ondone={done}/>{/if}
{#if finished}<p class="notice" role="status">{t.buDone}</p>{/if}
{#if error}<p class="error" role="alert">{t.errors[error as keyof typeof t.errors]??t.errors['request.failed']}</p>{/if}
</div>
</section>
