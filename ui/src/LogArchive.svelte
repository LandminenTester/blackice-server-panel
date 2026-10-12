<script lang="ts">
import {onMount} from 'svelte';import type {Catalog,Locale} from './locales';
let {t,locale,request}=$props<{t:Catalog;locale:Locale;request:(url:string,body?:object)=>Promise<any>}>();
type LogFile={name:string;size:number;modified:string;started:string|null};
let files=$state<LogFile[]>([]),selected=$state(''),content=$state(''),loading=$state(true),error=$state('');
const day=(f:LogFile)=>new Date(f.started??f.modified).toLocaleDateString(locale,{dateStyle:'full'});
let groups=$derived.by(()=>{const map=new Map<string,LogFile[]>();for(const f of files){const k=day(f);map.set(k,[...(map.get(k)??[]),f]);}return [...map.entries()];});
const when=(f:LogFile)=>new Date(f.started??f.modified).toLocaleTimeString(locale);
const size=(n:number)=>n<1024?n+' B':n<1048576?(n/1024).toFixed(1)+' KB':(n/1048576).toFixed(1)+' MB';
async function load(){loading=true;error='';try{files=await request('/api/log-files');}catch(e){error=(e as Error).message;}finally{loading=false;}}
async function open(name:string){selected=name;content='…';try{const r=await fetch('/api/log-files/'+encodeURIComponent(name));content=r.ok?await r.text():t.errors['request.failed'];}catch{content=t.errors['request.failed'];}}
onMount(load);
</script>
<section class="log-archive" aria-label={t.logArchive}>
<div class="log-archive-head"><p class="muted">{t.logArchiveHint}</p><div class="actions"><button onclick={load} disabled={loading}>{t.refresh}</button><a class="button primary" href="/api/log-files.zip" download aria-disabled={!files.length}>{t.downloadAllZip}</a></div></div>
{#if error}<p class="error" role="alert">{t.errors[error as keyof typeof t.errors]??t.errors['request.failed']}</p>{/if}
<div class="log-archive-body"><nav class="log-list" aria-label={t.logArchive}>{#if !files.length&&!loading}<p class="empty">{t.noLogFiles}</p>{/if}{#each groups as [label,items]}<h3>{label}</h3><ul>{#each items as f}<li><button class:active={selected===f.name} onclick={()=>open(f.name)}><strong>{t.serverStart} {when(f)}</strong><small>{f.name} · {size(f.size)}</small></button></li>{/each}</ul>{/each}</nav>
<div class="log-view">{#if selected}<div class="log-view-head"><strong>{selected}</strong><a href={'/api/log-files/'+encodeURIComponent(selected)} download={selected}>{t.downloadFile}</a></div><textarea class="console-output" readonly aria-label={selected} value={content}></textarea>{:else}<p class="empty">{t.selectLogFile}</p>{/if}</div></div>
</section>
