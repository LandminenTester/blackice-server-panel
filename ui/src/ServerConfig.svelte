<script lang="ts">
import {onMount} from 'svelte';import type {Catalog,Locale} from './locales';import Toggle from './components/Toggle.svelte';import {regionOptions} from './regions';
let {t,locale,request}=$props<{t:Catalog;locale:Locale;request:(url:string,body?:object)=>Promise<any>}>();
type Values={name:string;description:string;dlc:string;region:string;bindAddress:string;endpoint:string;lan:boolean};
let values=$state<Values>({name:'',description:'',dlc:'',region:'',bindAddress:'',endpoint:'',lan:false}),exists=$state(true),running=$state(false),loaded=$state(false),busy=$state(false),saved=$state(false),error=$state('');
let regions=$derived(regionOptions(locale));
let valid=$derived(values.name.length>0&&values.name.length<=128&&values.description.length<=512&&/^[A-Z]{3}$/.test(values.region)&&!/["\]/.test(values.name+values.description)&&/^[a-z0-9_-]{0,32}$/.test(values.dlc));
function apply(data:any){exists=data.exists;running=data.serverRunning;const v=data.values;values={name:v.name,description:v.description,dlc:v.dlc,region:v.region,bindAddress:v.bindAddress,endpoint:v.endpoint,lan:v.lan};}
async function load(){error='';try{apply(await request('/api/server-config'));loaded=true;}catch(e){error=(e as Error).message;}}
async function save(){busy=true;error='';saved=false;try{apply(await request('/api/server-config',values));saved=true;}catch(e){error=(e as Error).message;}finally{busy=false;}}
onMount(load);
</script>
<section class="update-panel server-config" aria-label={t.scTitle}>
<div class="update-heading"><h2>{t.scTitle}</h2><button disabled={busy} onclick={load}>{t.refresh}</button></div>
<p class="muted">{t.scHint}</p>
{#if !exists&&loaded}<p class="error" role="alert">{t.errors['serverConfig.missing']}</p>{:else}
<form class="config-grid" onsubmit={(e)=>{e.preventDefault();save();}}>
<label>{t.scName}<input bind:value={values.name} maxlength="128" required disabled={busy||!loaded} oninput={()=>saved=false}/></label>
<label>{t.scDesc}<textarea bind:value={values.description} maxlength="512" rows="3" disabled={busy||!loaded} oninput={()=>saved=false}></textarea></label>
<label>{t.scRegion}<select bind:value={values.region} disabled={busy||!loaded} onchange={()=>saved=false}>{#if values.region&&!regions.some(r=>r.value===values.region)}<option value={values.region}>{values.region}</option>{/if}{#each regions as r}<option value={r.value}>{r.label}</option>{/each}</select><small>{t.scRegionHint}</small></label>
<label>{t.scDlc}<input bind:value={values.dlc} maxlength="32" pattern="[a-z0-9_\-]*" disabled={busy||!loaded} oninput={()=>saved=false}/></label>
<label>{t.scBind}<input bind:value={values.bindAddress} maxlength="255" placeholder="[::]:20770" disabled={busy||!loaded} oninput={()=>saved=false}/></label>
<label>{t.scEndpoint}<input bind:value={values.endpoint} maxlength="255" placeholder="203.0.113.5:20770" disabled={busy||!loaded} oninput={()=>saved=false}/></label>
<Toggle checked={values.lan} label={t.scLan} disabled={busy||!loaded} onchange={v=>{values.lan=v;saved=false;}}/>
<div class="actions"><button class="primary" disabled={busy||!loaded||!valid}>{busy?t.loading:t.scSave}</button></div>
</form>
{#if saved}<p class="notice" role="status">{running?t.scSavedRestart:t.scSaved}</p>{/if}
{#if error}<p class="error" role="alert">{t.errors[error as keyof typeof t.errors]??t.errors['request.failed']}</p>{/if}{/if}
</section>
