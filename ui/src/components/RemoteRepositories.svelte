<script lang="ts">
import type {Catalog} from '../locales';import SearchSelect from './SearchSelect.svelte';
type Account={id:string;label:string;type:string;hasToken:boolean};type Repo={name:string;fullName:string;cloneUrl:string;webUrl:string;defaultBranch:string;private:boolean};
let {t,request,accounts,onchoose}=$props<{t:Catalog;request:(url:string,body?:object)=>Promise<any>;accounts:Account[];onchoose:(repo:Repo,accountId:string)=>void}>();
let account=$state(''),owner=$state(''),owners=$state<{id:string;name:string}[]>([]),query=$state(''),repos=$state<Repo[]>([]),page=$state(1),more=$state(false),busy=$state(false),error=$state(''),searched=$state(false);
let generation=0;
async function ownersFor(id:string){account=id;owner='';owners=[];repos=[];searched=false;const gen=++generation;busy=true;error='';try{const data=await request('/api/bundle-tools/remote/owners',{accountId:id});if(gen===generation)owners=data;}catch(e){if(gen===generation)error=(e as Error).message;}finally{if(gen===generation)busy=false;}}
async function search(next=1){const gen=++generation;busy=true;error='';try{const data=await request('/api/bundle-tools/remote/repositories',{accountId:account,owner,query,page:next});if(gen===generation){repos=data.repositories;page=data.page;more=data.hasMore;searched=true;}}catch(e){if(gen===generation)error=(e as Error).message;}finally{if(gen===generation)busy=false;}}
</script>
<section class="remote-browser" aria-label={t.bxDiscover}><h3>{t.bxDiscover}</h3>
<div class="field"><span>{t.bpAccount}</span><SearchSelect value={account} label={t.bpAccount} options={accounts.filter((a:Account)=>a.hasToken).map((a:Account)=>({value:a.id,label:a.label+' · '+a.type}))} disabled={busy} onchange={ownersFor}/></div>
{#if owners.length}<div class="field"><span>{t.bxOwner}</span><SearchSelect value={owner} label={t.bxOwner} options={owners.map(o=>({value:o.id,label:o.name}))} onchange={v=>{owner=v;repos=[];searched=false;}} disabled={busy}/></div>{/if}
<div class="remote-search"><label><span class="sr-only">{t.bxRemoteSearch}</span><input type="search" bind:value={query} maxlength="100" placeholder={t.bxRemoteSearch} onkeydown={e=>{if(e.key==='Enter'){e.preventDefault();if(account&&!busy)search();}}}/></label><button type="button" disabled={!account||busy||!owners.length} onclick={()=>search()}>{busy?t.loading:t.bxSearchAction}</button></div>
<p class="muted">{t.bxRemoteHint}</p>
<ul class="remote-repos" aria-busy={busy}>{#each repos as repo}<li><button type="button" onclick={()=>onchoose(repo,account)}><strong>{repo.fullName}</strong><span>{repo.private?t.bpPrivate:t.bpPublic} · {repo.defaultBranch}</span></button></li>{/each}</ul>
{#if searched&&!repos.length&&!busy}<p class="empty">{t.bxNoResults}</p>{/if}
{#if searched}<div class="remote-pagination"><button type="button" disabled={busy||page<=1} onclick={()=>search(page-1)}>{t.bxPrevious}</button><span>{t.bxPage} {page}</span><button type="button" disabled={busy||!more} onclick={()=>search(page+1)}>{t.bxNext}</button></div>{/if}
{#if error}<p class="error" role="alert">{t.errors[error as keyof typeof t.errors]??t.errors['request.failed']}</p>{/if}
</section>
