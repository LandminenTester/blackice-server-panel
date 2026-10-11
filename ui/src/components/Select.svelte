<script lang="ts">
let {value,options,label,disabled=false,onchange=(_:string)=>{}}=$props<{value:string;options:{value:string;label:string}[];label:string;disabled?:boolean;onchange?:(value:string)=>void}>();
let open=$state(false);let trigger:HTMLButtonElement;let group=$state<HTMLDivElement>()!;
function choose(v:string){onchange(v);open=false;trigger.focus();}
function keys(e:KeyboardEvent){const buttons=[...group.querySelectorAll<HTMLButtonElement>('[role=option]')];let index=buttons.indexOf(document.activeElement as HTMLButtonElement);if(e.key==='Escape'){open=false;trigger.focus();}else if(['ArrowDown','ArrowUp','Home','End'].includes(e.key)){e.preventDefault();index=e.key==='Home'?0:e.key==='End'?buttons.length-1:(index+(e.key==='ArrowDown'?1:-1)+buttons.length)%buttons.length;buttons[index]?.focus();}}
</script>
<div class="select-wrap">
<button type="button" class="select-trigger" bind:this={trigger} {disabled} aria-label={label} aria-haspopup="listbox" aria-expanded={open} onclick={()=>{open=!open;if(open)requestAnimationFrame(()=>group.querySelector<HTMLButtonElement>('[aria-selected=true]')?.focus());}}>{options.find((x:{value:string;label:string})=>x.value===value)?.label??value}<svg viewBox="0 0 16 16" aria-hidden="true"><path d="m4 6 4 4 4-4"/></svg></button>
{#if open}<div class="select-options" role="listbox" aria-label={label} bind:this={group} tabindex="-1" onkeydown={keys} onfocusout={(e)=>{if(!e.currentTarget.parentElement?.contains(e.relatedTarget as Node))open=false;}}>{#each options as option}<button type="button" role="option" aria-selected={option.value===value} class:selected={option.value===value} onclick={()=>choose(option.value)}>{option.label}</button>{/each}</div>{/if}
</div>
