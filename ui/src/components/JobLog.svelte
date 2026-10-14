<script lang="ts">
import {untrack} from 'svelte';import type {Catalog} from '../locales';
let {t,request,jobId,ondone=(_:string)=>{}}=$props<{t:Catalog;request:(url:string,body?:object)=>Promise<any>;jobId:string;ondone?:(state:string)=>void}>();
type Job={id:string;kind:string;target:string;state:string;phase:string;error?:string|null;bytes:number;total:number;result?:string|null};
let job=$state<Job|null>(null),log=$state<string[]>([]),failed=$state(''),paused=$state(false),follow=$state(true),query=$state(''),box:HTMLDivElement;
const lines=$derived(log.filter(line=>line.toLowerCase().includes(query.toLowerCase())));
const stateLabel=(state:string)=>state==='succeeded'?t.bpOk:state==='failed'?t.bpFailed:state==='cancelled'?t.cancel:t.toolBusy;
function phaseLabel(value:string){if(t.jobPhases[value])return t.jobPhases[value];const prefix=Object.keys(t.jobPhases).find(p=>p.endsWith(' ')&&value.startsWith(p));return prefix?t.jobPhases[prefix]+value.slice(prefix.length):value;}
function download(){const url=URL.createObjectURL(new Blob([lines.join('\n')],{type:'text/plain;charset=utf-8'}));const a=document.createElement('a');a.href=url;a.download='bundle-job-'+jobId+'.log';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);}
$effect(()=>{const id=jobId;let stopped=false,polling=false,notified=false;untrack(()=>{job=null;log=[];failed='';paused=false;});
 async function poll(){if(stopped||polling||paused)return;polling=true;try{const r=await request('/api/bundle-tools/jobs/'+id);if(stopped)return;job=r.job;log=r.log;failed='';if(follow)requestAnimationFrame(()=>{if(box)box.scrollTop=box.scrollHeight;});if(job&&job.state!=='running'&&!notified){notified=true;ondone(job.state);}}catch(e){if(!stopped)failed=(e as Error).message;}finally{polling=false;}}
 untrack(poll);const timer=setInterval(()=>{if(!job||job.state==='running')poll();},1000);return()=>{stopped=true;clearInterval(timer);};});
</script>
<section class="job-log" aria-label={t.bxJob}>
<div class="job-toolbar"><div><h3>{job?.target??t.bxJob}</h3><p class="job-state" role="status"><span class={'job-badge '+(job?.state??'running')}>{stateLabel(job?.state??'running')}</span>{#if !job||job.phase!==job.state}<span>{job?phaseLabel(job.phase):t.bxPending}</span>{/if}</p></div><div class="actions"><button class:active={follow} aria-pressed={follow} onclick={()=>follow=!follow}>{t.bxFollow}</button><button aria-pressed={paused} disabled={!!job&&job.state!=='running'} onclick={()=>paused=!paused}>{paused?t.bxResume:t.bxPause}</button><button disabled={!log.length} onclick={download}>{t.bxDownload}</button></div></div>
<label class="log-filter"><span class="sr-only">{t.bxLogSearch}</span><input type="search" bind:value={query} placeholder={t.bxLogSearch}/></label>
{#if job&&job.total>0&&job.state==='running'}<progress max={job.total} value={job.bytes} aria-label={phaseLabel(job.phase)}></progress>{/if}
<div class="job-lines" role="textbox" aria-readonly="true" aria-multiline="true" tabindex="0" aria-label={t.bxJob} bind:this={box}>{#each lines as line}<span class:log-error={/error|failed|exception/i.test(line)} class:log-phase={line.startsWith('==')}>{line}{'\n'}</span>{/each}{#if !log.length}{t.bxPending}{/if}</div>
{#if job?.result}<p class="job-result">{#if /^https:\/\//.test(job.result)}<a href={job.result} target="_blank" rel="noopener noreferrer">{job.result}</a>{:else}{phaseLabel(job.result)}{/if}</p>{/if}
{#if job?.error||failed}<p class="error" role="alert">{t.errors[(job?.error||failed) as keyof typeof t.errors]??t.errors['request.failed']}</p>{/if}
</section>
