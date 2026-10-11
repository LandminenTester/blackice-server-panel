<script lang="ts">let {checked,label,onchange,disabled=false}=$props<{checked:boolean;label:string;onchange:(value:boolean)=>void;disabled?:boolean}>();</script>
<button type="button" {disabled} role="switch" aria-checked={checked} class="toggle" onclick={()=>onchange(!checked)}><span class:checked aria-hidden="true"><i></i></span>{label}</button>
