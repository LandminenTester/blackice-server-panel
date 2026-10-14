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
let overwriteDialog:HTMLDialogElement;
async function requestInstall(){if(!file)return;busy=true;error='';try{const r=await request('/api/bundle-tools/upload/exists',{name});file.exists=r.exists;if(r.exists){replace=false;overwriteDialog.showModal();}else{replace=false;await install();}}catch(e){error=(e as Error).message;}finally{if(!jobId)busy=false;}}
let nameValid=$derived(/^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$/.test(name));
</script>
<section class="bundle-panel bundle-upload" aria-label={t.buUploadTitle}>
<div class="section-heading"><div><h2>{t.buUploadTitle} <Experimental label={t.experimental} hint={t.experimentalHint}/></h2><p class="muted">{t.buUploadHint.replace('{mb}',String(maxMb))}</p></div></div>
<div class="bundle-upload-body"><ol class="upload-stages"><li class:active={!file&&!jobId}>{t.bvUpload}</li><li class:active={!!file}>{t.bxArchive}</li><li class:active={!!jobId}>{t.bxInstall}</li></ol>
{#if !file&&!jobId}<label class="dropzone" class:drag ondragover={e=>{e.preventDefault();drag=true;}} ondragleave={()=>drag=false} ondrop={e=>{e.preventDefault();drag=false;choose(e.dataTransfer?.files?.[0]);}}><input type="file" accept=".zip,application/zip" disabled={busy} onchange={e=>choose((e.currentTarget as HTMLInputElement).files?.[0])}/><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 16V3m-5 5 5-5 5 5M4 15v6h16v-6"/></svg><strong>{busy?t.buUploading+' '+percent+'%':t.buDrop}</strong><small>{t.buChoose}</small></label>{#if busy}<progress max="100" value={percent} aria-label={t.buUploading}></progress>{/if}{/if}
{#if file}<div class="upload-layout"><section><h3>{file.original}</h3><div class="project-badges"><span class="job-badge">{size(file.report.size)}</span><span class="job-badge">{file.report.files} {t.buFiles}</span><span class={'job-badge '+(file.report.ok?'succeeded':'failed')}>{file.report.ok?t.bxReady:t.bpFailed}</span></div>
{#if file.report.meta}<dl class="detail-facts"><dt>{t.buAuthor}</dt><dd>{file.report.meta.author??'—'}</dd><dt>{t.buVersion}</dt><dd>{file.report.meta.version??'—'}</dd><dt>{t.buDescription}</dt><dd>{file.report.meta.description??'—'}</dd></dl>{/if}
{#if file.report.ok}<p class="muted">{t.buStructureOnly}</p><label>{t.buName}<input bind:value={name} maxlength="128" aria-invalid={!nameValid} disabled={busy}/></label>{#if file.exists}<p class="notice-inline">{t.buExists}</p>{/if}<div class="actions"><button onclick={()=>file=null} disabled={busy}>{t.cancel}</button><button class="primary" disabled={busy||!nameValid} onclick={requestInstall}>{t.buInstall}</button></div>{:else}<button onclick={()=>file=null}>{t.buTryAgain}</button>{/if}</section>
<section><h3>{t.buChecks}</h3><ul class="checks upload-checks">{#each file.report.checks as c}<li class={c.status}><span aria-hidden="true">{icon(c.status)}</span><span>{c.detail??c.id}</span></li>{/each}</ul></section></div>{/if}
{#if jobId}<JobLog {t} {request} {jobId} ondone={done}/>{/if}{#if finished}<p class="notice" role="status">{t.buDone}</p><button onclick={()=>{jobId='';finished=false;}}>{t.buTryAgain}</button>{/if}{#if error}<p class="error" role="alert">{t.errors[error as keyof typeof t.errors]??t.errors['request.failed']}</p>{/if}
</div></section>

<dialog bind:this={overwriteDialog}><h2>{t.bundleOverwrite}</h2><p>{t.bundleOverwriteHint.replace('{name}',name)}</p><div class="actions"><button onclick={()=>overwriteDialog.close()}>{t.cancel}</button><button class="danger" onclick={()=>{replace=true;overwriteDialog.close();install();}}>{t.bundleOverwriteAction}</button></div></dialog>
