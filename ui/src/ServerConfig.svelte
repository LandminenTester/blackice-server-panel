<script lang="ts">
import {onMount} from 'svelte';import type {Catalog,Locale} from './locales';import Toggle from './components/Toggle.svelte';import {regionOptions,regionCodes} from './regions';
let {t,locale,request,backend='windows',onsaved=()=>{}}=$props<{t:Catalog;locale:Locale;request:(url:string,body?:object)=>Promise<any>;backend?:string;onsaved?:()=>void}>();
type Values={name:string;description:string;dlc:string;region:string;bindAddress:string;voipAddress:string;endpoint:string;lan:boolean;privacy:boolean;ugcAutomaticRestarts:boolean;channel:string;autoUpdate:boolean;restartAfterUpdate:string};
const blank:Values={name:'',description:'',dlc:'',region:'',bindAddress:'',voipAddress:'',endpoint:'',lan:false,privacy:false,ugcAutomaticRestarts:false,channel:'alpha',autoUpdate:false,restartAfterUpdate:'auto'};
let values=$state<Values>({...blank}),exists=$state(true),running=$state(false),loaded=$state(false),busy=$state(false),saved=$state(false),error=$state(''),regionText=$state(''),tab=$state('general');
let regions=$derived(regionOptions(locale));
function toCode(text:string){const raw=text.trim();const upper=raw.slice(0,3).toUpperCase();if(regionCodes.includes(upper)&&(raw.length===3||/^[A-Za-z]{3}\s*[–-]/.test(raw)))return upper;const hit=regions.find(r=>r.label.toLocaleLowerCase(locale)===raw.toLocaleLowerCase(locale)||r.label.slice(6).toLocaleLowerCase(locale)===raw.toLocaleLowerCase(locale));return hit?.value??'';}
let regionCode=$derived(toCode(regionText));
let valid=$derived(values.name.length>0&&values.name.length<=128&&values.description.length<=512&&regionCode!==''&&!/["\\]/.test(values.name+values.description)&&/^[a-z0-9_-]{0,32}$/.test(values.dlc)&&/^[a-z0-9_-]{1,32}$/.test(values.channel)&&/^[a-z0-9_-]{1,32}$/.test(values.restartAfterUpdate));
function labelFor(code:string){return regions.find(r=>r.value===code)?.label??code;}
function apply(data:any){exists=data.exists;running=data.serverRunning;values={...blank,...data.values};regionText=values.region?labelFor(values.region):'';}
async function load(){error='';try{apply(await request('/api/server-config'));loaded=true;}catch(e){error=(e as Error).message;}}
async function save(){busy=true;error='';saved=false;try{apply(await request('/api/server-config',{...values,region:regionCode}));saved=true;onsaved();}catch(e){error=(e as Error).message;}finally{busy=false;}}
const dirty=()=>saved=false;
onMount(load);
</script>
<section class="update-panel server-config" aria-label={t.scTitle}>
<div class="update-heading"><h2>{t.scTitle}</h2><div class="actions"><a class="button" href="https://docs.replay.re/servers/configuration/" target="_blank" rel="noopener noreferrer">{t.scDocs} ↗</a><button disabled={busy} onclick={load}>{t.refresh}</button></div></div>
<p class="muted">{t.scHint}</p>
{#if !exists&&loaded}<p class="error" role="alert">{t.errors['serverConfig.missing']}</p>{:else}
<div class="subtabs inner" role="tablist" aria-label={t.scTitle}>{#each [['general',t.scGeneral],['network',t.scNetwork],['bundles',t.scBundles],['updates',t.scUpdates]] as [id,label]}<button type="button" role="tab" aria-selected={tab===id} class:active={tab===id} onclick={()=>tab=id}>{label}</button>{/each}</div>
<form class="config-grid" onsubmit={(e)=>{e.preventDefault();save();}}>
<div class="tab-body" hidden={tab!=='general'}>
<label>{t.scName}<input bind:value={values.name} maxlength="128" required disabled={busy||!loaded} oninput={dirty}/></label>
<label>{t.scDesc}<textarea bind:value={values.description} maxlength="512" rows="3" disabled={busy||!loaded} oninput={dirty}></textarea></label>
<label>{t.scRegion}<input list="region-list" bind:value={regionText} placeholder="DEU – Germany" autocomplete="off" disabled={busy||!loaded} oninput={dirty} aria-invalid={regionText!==''&&regionCode===''}/><datalist id="region-list">{#each regions as r}<option value={r.label}></option>{/each}</datalist><small>{regionText!==''&&regionCode===''?t.errors['serverConfig.region']:t.scRegionHint}</small></label>
<label>{t.scDlc}<input list="dlc-list" bind:value={values.dlc} maxlength="32" pattern="[a-z0-9_\-]*" placeholder="ep1" disabled={busy||!loaded} oninput={dirty}/><datalist id="dlc-list"><option value="ep1"></option></datalist><small>{t.scDlcHint}</small></label>
<Toggle checked={values.privacy} label={t.scPrivacy} disabled={busy||!loaded} onchange={v=>{values.privacy=v;dirty();}}/>
</div>
<div class="tab-body" hidden={tab!=='network'}>
<label>{t.scBind}<input bind:value={values.bindAddress} maxlength="255" placeholder="[::]:20770" disabled={busy||!loaded} oninput={dirty}/></label>
<label>{t.scVoip}<input bind:value={values.voipAddress} maxlength="255" placeholder="[::]:20771" disabled={busy||!loaded} oninput={dirty}/></label>
<label>{t.scEndpoint}<input bind:value={values.endpoint} maxlength="255" placeholder="203.0.113.5:20770" disabled={busy||!loaded} oninput={dirty}/></label>
<Toggle checked={values.lan} label={t.scLan} disabled={busy||!loaded} onchange={v=>{values.lan=v;dirty();}}/>
</div>
<div class="tab-body" hidden={tab!=='bundles'}>
<Toggle checked={values.ugcAutomaticRestarts} label={t.scUgc} disabled={busy||!loaded} onchange={v=>{values.ugcAutomaticRestarts=v;dirty();}}/>
</div>
<div class="tab-body" hidden={tab!=='updates'}>
<p class="muted">{t.scUpdatesHint}{backend==='docker'?' '+t.scDockerEnv:''}</p>
<label>{t.scChannel}<input bind:value={values.channel} maxlength="32" pattern="[a-z0-9_\-]+" required disabled={busy||!loaded} oninput={dirty}/></label>
<Toggle checked={values.autoUpdate} label={t.scAutoUpdate} disabled={busy||!loaded} onchange={v=>{values.autoUpdate=v;dirty();}}/>
<label>{t.scRestartAfter}<input list="restart-list" bind:value={values.restartAfterUpdate} maxlength="32" pattern="[a-z0-9_\-]+" required disabled={busy||!loaded} oninput={dirty}/><datalist id="restart-list"><option value="auto"></option><option value="never"></option><option value="always"></option></datalist></label>
</div>
<div class="actions"><button class="primary" disabled={busy||!loaded||!valid}>{busy?t.loading:t.scSave}</button></div>
</form>
{#if saved}<p class="notice" role="status">{running?t.scSavedRestart:t.scSaved}</p>{/if}
{#if error}<p class="error" role="alert">{t.errors[error as keyof typeof t.errors]??t.errors['request.failed']}</p>{/if}{/if}
</section>
