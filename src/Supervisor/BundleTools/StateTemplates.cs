namespace ReplayPanel.Supervisor.BundleTools;
public static class StateTemplates
{
    public static void Validate(UiOptions ui){
        var allowed=ui.Framework switch{"react"=>new[]{"none","zustand","jotai","nanostores"},"vue"=>["none","pinia","nanostores"],"svelte"=>["none","runes","nanostores"],_=>[]};
        if(allowed.Length==0||ui.StateLibrary is not null&&!allowed.Contains(ui.StateLibrary))throw new InvalidOperationException("bundleTools.package");
    }
    public static void Apply(UiOptions ui,Dictionary<string,string> files,SortedDictionary<string,string> dependencies){
        var ext=ui.TypeScript?"ts":"js";
        if(ui.StateLibrary=="jotai"){
            dependencies.Remove("zustand");dependencies["jotai"]="3.0.1";
            files["src/store."+ext]="import { atom, useAtom } from 'jotai'\n\nconst counter = atom(0)\nexport function useCounter() {\n  const [value, setValue] = useAtom(counter)\n  return { value, increment: () => setValue((n) => n + 1) }\n}\n";
        }
        if(ui.StateLibrary!="nanostores")return;
        dependencies["nanostores"]="1.5.5";
        if(ui.Framework=="react"){
            dependencies.Remove("zustand");dependencies["@nanostores/react"]="2.0.1";
            files["src/store."+ext]="import { atom } from 'nanostores'\nimport { useStore } from '@nanostores/react'\n\nconst counter = atom(0)\nexport function useCounter() {\n  const value = useStore(counter)\n  return { value, increment: () => counter.set(counter.get() + 1) }\n}\n";
        }else if(ui.Framework=="vue"){
            dependencies.Remove("pinia");dependencies["@nanostores/vue"]="1.1.0";
            // Type-check application code; upstream declarations currently conflict with TypeScript 6.
            if(ui.TypeScript){var json=System.Text.Json.Nodes.JsonNode.Parse(files["tsconfig.json"])!;json["compilerOptions"]!["skipLibCheck"]=true;files["tsconfig.json"]=json.ToJsonString(new(){WriteIndented=true});}
            files["src/main."+ext]=files["src/main."+ext].Replace("import { createPinia } from 'pinia'\n","").Replace(".use(createPinia())","");
            files["src/stores/counter."+ext]="import { atom } from 'nanostores'\nimport { useStore } from '@nanostores/vue'\nimport { computed, reactive } from 'vue'\n\nconst counter = atom(0)\nexport function useCounter() {\n  const state = useStore(counter)\n  return reactive({ value: computed({ get: () => state.value, set: (n) => counter.set(n) }) })\n}\n";
        }else{
            files["src/store.svelte."+ext]="import { atom } from 'nanostores'\nexport const counter = atom(0)\n";
            files["src/App.svelte"]=files["src/App.svelte"].Replace("counter.value","$counter");
        }
    }
}
