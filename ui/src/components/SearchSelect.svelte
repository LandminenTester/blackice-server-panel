<script lang="ts">
let {value,options,label,placeholder='',disabled=false,onchange=(_:string)=>{}}=$props<{value:string;options:{value:string;label:string}[];label:string;placeholder?:string;disabled?:boolean;onchange?:(value:string)=>void}>();
let open=$state(false),query=$state(''),active=$state(0);let group:HTMLDivElement;const id=$props.id();
const filtered=$derived(options.filter((o:{value:string;label:string})=>o.label.toLocaleLowerCase().includes(query.toLocaleLowerCase())));
function choose(v:string){onchange(v);open=false;query='';}
function keys(e:KeyboardEvent){if(e.key==='Escape'){open=false;query='';}else if(e.key==='ArrowDown'||e.key==='ArrowUp'){e.preventDefault();open=true;active=Math.max(0,Math.min(filtered.length-1,active+(e.key==='ArrowDown'?1:-1)));}else if(e.key==='Enter'&&open){e.preventDefault();if(filtered[active])choose(filtered[active].value);}}
</script>
<div class="search-select" bind:this={group} onfocusout={(e)=>{if(!group.contains(e.relatedTarget as Node)){open=false;query='';}}}>
<input role="combobox" aria-label={label} aria-expanded={open} aria-controls={id+'-list'} aria-autocomplete="list" aria-activedescendant={open&&filtered[active]?id+'-'+active:undefined} {disabled} value={open?query:options.find((o:{value:string;label:string})=>o.value===value)?.label??''} placeholder={placeholder||label} onfocus={()=>{open=true;query='';active=0;}} oninput={e=>{query=e.currentTarget.value;open=true;active=0;}} onkeydown={keys}/>
{#if open}<div role="listbox" id={id+'-list'} aria-label={label} class="search-results">{#each filtered as option,i}<button type="button" id={id+'-'+i} role="option" aria-selected={value===option.value} class:active={i===active} onmousedown={e=>e.preventDefault()} onclick={()=>choose(option.value)}>{option.label}</button>{/each}</div>{/if}
</div>
