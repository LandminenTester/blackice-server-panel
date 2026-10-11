<script lang="ts">
  import { onMount } from 'svelte';
  type Status = { state: string; pid: number | null; serverRoot: string; shutdownMode: string; detail?: string };
  type Bundle = { name: string; state: 'running' | 'stopped' | 'unknown' };
  let authenticated = $state(false), ready = $state(false), csrf = $state('');
  let password = $state(''), error = $state(''), notice = $state(''), busy = $state(false);
  let status = $state<Status | null>(null), lines = $state<string[]>([]), logs = $state<string[]>([]);
  let connected = $state(false), command = $state(''), view = $state('console'), follow = $state(true);
  let output = $state<HTMLTextAreaElement>();
  let bundles = $state<Bundle[]>([]), bundleLoading = $state(false), bundleReady = $state(false), bundleControl = $state(false);
  let bundleError = $state(''), bundleCheckedAt = $state(''), activeBundle = $state('');
  let confirmDialog: HTMLDialogElement;
  let pendingAction = $state(''), stream: EventSource | undefined;
  const stateNames: Record<string, string> = { running: 'Server läuft', stopped: 'Server gestoppt', external: 'Extern gestartet', unknown: 'Status unbekannt' };
  const serverState = $derived(status?.state ?? 'unknown');
  const outputText = $derived((view === 'console' ? lines.filter(x => !/panel-query-[a-f0-9]{12}-(begin|end)/.test(x) && !/^\s*>*\s*bundles\s*$/.test(x)) : logs).join('\n') || (view === 'logs' ? 'Noch keine Logeinträge in der aktuellen Datei.' : serverState === 'external' ? 'Extern gestarteter Server. Die Logdatei ist verfügbar; die Live-Konsole benötigt einen Start über den Supervisor.' : serverState === 'running' ? 'Server läuft. Warte auf die erste Konsolenausgabe.' : serverState === 'stopped' ? 'Noch keine Ausgabe. Starte den Server, um seine Konsole hier zu sehen.' : 'Keine Verbindung zum Supervisor. Prüfe den lokalen Dienst.'));
  async function session() {
    const response = await fetch('/api/session');
    if (!response.ok) throw new Error('Panel nicht erreichbar. Seite neu laden.');
    const data = await response.json(); csrf = data.csrf; authenticated = data.authenticated;
  }
  async function post(url: string, body?: object) {
    const response = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF': csrf }, body: JSON.stringify(body ?? {}) });
    if (!response.ok) {
      const data = await response.json().catch(() => ({}));
      if (response.status === 401) throw new Error('Anmeldung erforderlich oder Passwort falsch.');
      if (response.status === 429) throw new Error('Zu viele Anmeldeversuche. Bitte eine Minute warten.');
      throw new Error(data.error ?? 'Aktion fehlgeschlagen. Bitte erneut versuchen.');
    }
    return response.json();
  }
  function connect() {
    stream?.close(); stream = new EventSource('/api/events');
    stream.onmessage = event => {
      const data = JSON.parse(event.data); connected = data.status.ok;
      if (data.status.ok) status = data.status.status;
      else { status = null; error = data.status.error; }
      lines = data.console.lines ?? [];
    };
    stream.onerror = () => { connected = false; };
  }
  async function login(event: SubmitEvent) {
    event.preventDefault(); busy = true; error = '';
    try { await post('/api/login', { password }); password = ''; await session(); connect(); }
    catch (e) { error = String((e as Error).message); }
    finally { busy = false; }
  }
  async function logout() {
    try { await post('/api/logout'); stream?.close(); authenticated = false; status = null; await session(); }
    catch (e) { error = (e as Error).message; }
  }
  async function action(name: string) {
    busy = true; error = ''; notice = '';
    try { const result = await post('/api/server/' + name); status = result.status; notice = 'Aktion abgeschlossen.'; }
    catch (e) { error = (e as Error).message; }
    finally { busy = false; }
  }
  function confirm(name: string) { pendingAction = name; confirmDialog.showModal(); }
  async function send(event: SubmitEvent) {
    event.preventDefault(); busy = true; error = '';
    try { await post('/api/command', { text: command }); command = ''; notice = 'Befehl an Replay gesendet.'; }
    catch (e) { error = (e as Error).message; }
    finally { busy = false; }
  }
  async function settings(event: Event) {
    busy = true; error = '';
    try { const result = await post('/api/settings', { shutdownMode: (event.target as HTMLSelectElement).value }); status = result.status; notice = 'Verhalten gespeichert.'; }
    catch (e) { error = (e as Error).message; }
    finally { busy = false; }
  }
  async function loadLogs() {
    error = '';
    try { const response = await fetch('/api/logs'); const data = await response.json(); if (!data.ok) throw new Error(data.error); logs = data.lines; view = 'logs'; }
    catch (e) { error = (e as Error).message; }
  }
  function receiveBundles(data: { ok: boolean; bundles: Bundle[]; canControl: boolean; checkedAt: string; error?: string }) {
    bundleControl = data.ok && data.canControl;
    bundleError = data.error ?? '';
    if (data.ok) { bundles = data.bundles; bundleCheckedAt = data.checkedAt; bundleReady = true; }
  }
  async function loadBundles() {
    if (bundleLoading || busy) return;
    bundleLoading = true;
    try {
      const response = await fetch('/api/bundles');
      if (!response.ok) throw new Error(response.status === 401 ? 'Bitte erneut anmelden.' : 'Bundle-Liste nicht erreichbar. Erneut aktualisieren.');
      receiveBundles(await response.json());
    } catch (e) { bundleError = (e as Error).message; bundleControl = false; }
    finally { bundleLoading = false; }
  }
  async function bundleAction(bundle: Bundle, action: string) {
    busy = true; activeBundle = bundle.name; error = ''; notice = '';
    try {
      receiveBundles(await post('/api/bundles/' + encodeURIComponent(bundle.name) + '/' + action));
      notice = bundle.name + (action === 'stop' ? ' wurde gestoppt.' : action === 'restart' ? ' wurde neu gestartet.' : ' wurde gestartet.');
    } catch (e) { error = (e as Error).message; bundleControl = false; }
    finally { busy = false; activeBundle = ''; }
    if (!bundleControl) await loadBundles();
  }
  function selectView(name: string) {
    view = name;
    if (name === 'bundles') loadBundles();
    if (name === 'logs') loadLogs();
  }
  function tabKeys(event: KeyboardEvent) {
    const names = ['console', 'logs', 'bundles'];
    let index = names.indexOf(view);
    if (event.key === 'ArrowRight') index = (index + 1) % names.length;
    else if (event.key === 'ArrowLeft') index = (index + names.length - 1) % names.length;
    else if (event.key === 'Home') index = 0;
    else if (event.key === 'End') index = names.length - 1;
    else return;
    event.preventDefault(); selectView(names[index]);
    (event.currentTarget as HTMLElement).querySelectorAll<HTMLButtonElement>('button')[index]?.focus();
  }
  onMount(() => {
    session().then(() => { if (authenticated) connect(); }).catch(e => error = e.message).finally(() => ready = true);
    const refresh = setInterval(() => { if (authenticated && view === 'bundles') loadBundles(); }, 10000);
    return () => { stream?.close(); clearInterval(refresh); };
  });
  $effect(() => { const count = lines.length; const latest = lines.at(-1); if (follow && output && view === 'console') { requestAnimationFrame(() => { if (output) output.scrollTop = output.scrollHeight; }); } void count; void latest; });
</script>

<svelte:head><meta name="theme-color" content="#121715" /></svelte:head>

{#if !ready}
  <main class="login"><p role="status">Panel wird verbunden …</p></main>
{:else if !authenticated}
  <main class="login">
    <div class="login-body">
      <div class="brand"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 5h8a5 5 0 0 1 0 10H6V5Zm0 10 8 4M6 10h8" /></svg><span>Replay</span></div>
      <h1>Dein Server.<br />Eine Konsole.</h1>
      <p class="muted">Melde dich an, um deine lokale Replay-Instanz zu verwalten.</p>
      <form onsubmit={login} class="login-form">
        <label for="password">Panel-Passwort</label>
        <input id="password" type="password" autocomplete="current-password" bind:value={password} required disabled={busy} />
        <button class="primary" disabled={busy}>{busy ? 'Anmelden …' : 'Anmelden'}</button>
      </form>
      {#if error}<p class="error" role="alert">{error}</p>{/if}
      <p class="help">Das lokale Passwort findest du in <code>.local/login.txt</code> im Panelprojekt.</p>
    </div>
    <footer>Serverpanel · Lokal auf Port 20772</footer>
  </main>
{:else}
  <header class="toolbar">
    <a class="brand" href="/" aria-label="Replay Serverpanel"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 5h8a5 5 0 0 1 0 10H6V5Zm0 10 8 4M6 10h8" /></svg><span>Replay <small>Serverpanel</small></span></a>
    <div class="connection"><span class:online={connected} class="dot"></span>{connected ? 'Supervisor verbunden' : 'Verbindung unterbrochen'}</div>
    <button class="quiet" onclick={logout}>Abmelden</button>
  </header>
  <main class="workspace">
    <section class="server-heading" aria-labelledby="server-title">
      <div><h1 id="server-title">Serverübersicht</h1><p class="muted">Prozess, Konsole und Laufzeit an einem Ort.</p><p class="mobile-runtime"><span class:online={connected && serverState === 'running'} class="dot"></span>{connected ? (stateNames[serverState] ?? serverState) : 'Supervisor nicht verbunden'}</p></div>
      <div class="actions">
        <button class="primary" disabled={busy || !connected || serverState !== 'stopped'} onclick={() => action('start')}>{busy ? 'Bitte warten …' : 'Server starten'}</button>
        <button disabled={busy || !connected || serverState !== 'running'} onclick={() => confirm('restart')}>Neustarten</button>
        <button disabled={busy || !connected || serverState !== 'running'} onclick={() => confirm('stop')}>Stoppen</button>
      </div>
    </section>
    {#if error}<p class="error banner" role="alert">{error}</p>{/if}
    {#if notice}<p class="notice" role="status">{notice}</p>{/if}
    <div class="work-grid">
      <section class="console" aria-label="Serverkonsole">
        <div class="console-toolbar">
          <div class="tabs" role="tablist" tabindex="-1" aria-label="Serveransichten" onkeydown={tabKeys}>
            <button id="tab-console" role="tab" aria-selected={view === 'console'} aria-controls="panel-console" tabindex={view === 'console' ? 0 : -1} class:active={view === 'console'} onclick={() => selectView('console')}>Live-Konsole</button>
            <button id="tab-logs" role="tab" aria-selected={view === 'logs'} aria-controls="panel-logs" tabindex={view === 'logs' ? 0 : -1} class:active={view === 'logs'} onclick={() => selectView('logs')}>Logdatei</button>
            <button id="tab-bundles" role="tab" aria-selected={view === 'bundles'} aria-controls="panel-bundles" tabindex={view === 'bundles' ? 0 : -1} class:active={view === 'bundles'} onclick={() => selectView('bundles')}>Bundles</button>
          </div>
          {#if view !== 'bundles'}<label class="follow"><input type="checkbox" bind:checked={follow} /> Ausgabe folgen</label>{/if}
        </div>
        {#if view === 'bundles'}
          <div id="panel-bundles" role="tabpanel" aria-labelledby="tab-bundles" class="bundle-panel" aria-busy={bundleLoading}>
            <div class="bundle-heading"><div><h2>Installierte Bundles</h2><p class="help">{bundleCheckedAt ? 'Geprüft um ' + new Date(bundleCheckedAt).toLocaleTimeString('de-DE') + ' · alle 10 Sekunden aktualisiert' : 'Der Status wird direkt beim Server abgefragt.'}</p></div><button class="quiet" onclick={loadBundles} disabled={bundleLoading || busy}>{bundleLoading ? 'Laden …' : 'Aktualisieren'}</button></div>
            {#if bundleError}<p class="bundle-message" role="status">{bundleError}</p>{/if}
            {#if !bundleReady && bundleLoading}<p class="bundle-message" role="status">Bundle-Liste wird geladen …</p>
            {:else if bundleReady && bundles.length === 0}<p class="bundle-message">Keine Bundles gefunden. Lege ein Bundle mit config.toml im Serverordner unter data/ugc ab und aktualisiere die Liste.</p>
            {/if}
            <ul class="bundle-list" aria-label="Bundle-Status">
              {#each bundles as bundle (bundle.name)}
                <li class="bundle-row"><div class="bundle-identity"><span class="bundle-name">{bundle.name}</span><span class="bundle-state"><span class="dot" class:online={bundle.state === 'running' && bundleControl && connected && serverState === 'running'}></span>{!connected ? 'Status unbekannt' : serverState === 'stopped' ? 'Gestoppt' : !bundleControl ? 'Status unbekannt' : bundle.state === 'running' ? 'Gestartet' : bundle.state === 'stopped' ? 'Gestoppt' : 'Status unbekannt'}</span></div>
                  <div class="bundle-actions" role="group" aria-label={'Aktionen für ' + bundle.name}>
                    {#if activeBundle === bundle.name}<span class="help" role="status">Aktion läuft …</span>{/if}
                    {#if bundle.state === 'running' && serverState !== 'stopped'}<button onclick={() => bundleAction(bundle, 'restart')} disabled={busy || bundleLoading || !bundleControl || !connected || serverState !== 'running'}>Neustarten</button><button class="quiet" onclick={() => bundleAction(bundle, 'stop')} disabled={busy || bundleLoading || !bundleControl || !connected || serverState !== 'running'}>Stoppen</button>
                    {:else}<button class="primary" onclick={() => bundleAction(bundle, 'start')} disabled={busy || bundleLoading || !bundleControl || !connected || serverState !== 'running' || bundle.state !== 'stopped'}>Starten</button>{/if}
                  </div>
                </li>
              {/each}
            </ul>
          </div>
        {:else}
        <div id={view === 'console' ? 'panel-console' : 'panel-logs'} role="tabpanel" aria-labelledby={view === 'console' ? 'tab-console' : 'tab-logs'}>
        <textarea class="console-output" bind:this={output} readonly aria-label={view === 'console' ? 'Konsolenausgabe' : 'Logausgabe'} value={outputText}></textarea>
        <form class="command-form" onsubmit={send}>
          <label class="sr-only" for="command">Replay-Konsolenbefehl</label>
          <span aria-hidden="true" class="prompt">›</span>
          <input id="command" bind:value={command} placeholder="Replay-Befehl eingeben, z. B. bundles" autocomplete="off" maxlength="1024" disabled={busy || !connected || serverState !== 'running'} />
          <button disabled={busy || !connected || serverState !== 'running' || !command.trim()}>Senden</button>
        </form>
        </div>
        {/if}
      </section>
      <aside class="details">
        <section><h2>Laufzeit</h2><p class="server-state"><span class:online={serverState === 'running'} class="dot"></span>{stateNames[serverState] ?? serverState}</p><dl><dt>Prozess-ID</dt><dd>{status?.pid ?? '—'}</dd><dt>Backend</dt><dd>Windows · ConPTY</dd><dt>Serverordner</dt><dd class="path">{status?.serverRoot ?? 'Nicht verbunden'}</dd></dl>{#if status?.detail}<p class="help">{status.detail}</p>{/if}</section>
        <section><h2>Panel beenden</h2><label for="shutdown">Verhalten des Spielservers</label><select id="shutdown" value={status?.shutdownMode ?? 'keep-running'} onchange={settings} disabled={busy || !connected}><option value="keep-running">Server weiterlaufen lassen</option><option value="stop-server">Server geordnet stoppen</option></select><p class="help">Gilt beim Beenden des Webhosts. Das Schließen dieses Browser-Tabs verändert den Server nicht.</p></section>
        <section><h2>Wenn der Server hängt</h2><p class="help">Versuche zuerst den normalen Stop. Ein erzwungener Stop kann ungespeicherte Änderungen verlieren.</p><button class="danger quiet" disabled={busy || !connected || serverState !== 'running'} onclick={() => confirm('force-stop')}>Erzwungen stoppen …</button></section>
      </aside>
    </div>
    <footer class="workspace-footer">Spiel 20770 · Voice 20771 <span>Eigenständig · Ohne RP-Datenbank nutzbar</span></footer>
  </main>
{/if}

<dialog bind:this={confirmDialog}>
  <h2>{pendingAction === 'restart' ? 'Server neu starten?' : pendingAction === 'force-stop' ? 'Server erzwungen stoppen?' : 'Server stoppen?'}</h2>
  <p>{pendingAction === 'force-stop' ? 'Der Prozess wird sofort beendet. Ungespeicherte Daten können verloren gehen.' : 'Verbundene Spieler werden getrennt. Replay erhält zunächst den Befehl exit.'}</p>
  <div class="dialog-actions"><button onclick={() => confirmDialog.close()}>Abbrechen</button><button class:danger={pendingAction === 'force-stop'} class="primary" onclick={() => { confirmDialog.close(); action(pendingAction); }}>Bestätigen</button></div>
</dialog>
