<script lang="ts">
import {onMount} from 'svelte';
import type {Catalog} from '../locales';
let {t,request,jobId,ondone=(_:string)=>{}}=$props<{t:Catalog;request:(url:string,body?:object)=>Promise<any>;jobId:string;ondone?:(state:string)=>void}>();
type Job={id:string;kind:string;target:string;state:string;phase:string;error?:string|null;bytes:number;total:number;result?:string|null};
let job=$state<Job|null>(null),log=$state<string[]>([]),failed=$state(''),notified=false,box=$state<HTMLPreElement>();
async function poll(){
  try{const r=await request('/api/bundle-tools/jobs/'+jobId);job=r.job;log=r.log;
    requestAnimationFrame(()=>{if(box)box.scrollTop=box.scrollHeight;});
    if(job&&job.state!=='running'&&!notified){notified=true;ondone(job.state);}
  }catch(e){failed=(e as Error).message;}
}
onMount(()=>{poll();const timer=setInterval(()=>{if(!job||job.state==='running')poll();},1000);return()=>clearInterval(timer);});
</script>
<section class="job-log" aria-live="polite">
<p class="job-state"><strong>{job?.phase??'…'}</strong> · <span class={'job-badge '+(job?.state??'')}>{job?.state??'running'}</span></p>
{#if job&&job.total>0&&job.state==='running'}<progress max={job.total} value={job.bytes} aria-label={job.phase}></progress>{/if}
<pre class="job-lines" bind:this={box}>{log.join('\n')}</pre>
{#if job?.error||failed}<p class="error" role="alert">{t.errors[(job?.error||failed) as keyof typeof t.errors]??t.errors['request.failed']}</p>{/if}
</section>
