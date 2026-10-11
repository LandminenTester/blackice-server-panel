<script lang="ts">let {checked,label,onchange}=$props<{checked:boolean;label:string;onchange:(value:boolean)=>void}>();</script>
<button type="button" role="switch" aria-checked={checked} class="toggle" onclick={()=>onchange(!checked)}><span class:checked aria-hidden="true"><i></i></span>{label}</button>
