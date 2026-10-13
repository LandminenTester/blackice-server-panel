<script lang="ts">
  import { onMount } from "svelte";
  import { catalog, type Locale } from "./locales";
  import Select from "./components/Select.svelte";
  import Toggle from "./components/Toggle.svelte";
  import Users from "./Users.svelte";
  import Updates from "./Updates.svelte";
  import ServerUpdates from "./ServerUpdates.svelte";
  import ServerPath from "./ServerPath.svelte";
  import ServerConfig from "./ServerConfig.svelte";
  import BundleToolsSettings from "./BundleToolsSettings.svelte";
  import BundleUpload from "./BundleUpload.svelte";
  import BundleProjects from "./BundleProjects.svelte";
  import Bridge from "./Bridge.svelte";
  import HexBackground from "./HexBackground.svelte";
  import DiagnosticStream from "./DiagnosticStream.svelte";
  let permissions = $state<string[]>([]);
  const can = (permission: string) => permissions.includes(permission);
  let navItems = $derived(["dashboard", ...(can("console.read") || can("console.write") || can("logs.read") ? ["console"] : []), ...(can("bundles.read") ? ["bundles"] : []), "settings", ...(can("users.manage") ? ["users"] : [])]);
  type Status = {
    state: string;
    pid: number | null;
    serverRoot: string;
    shutdownMode: string;
    autoStartServer: boolean;
    autoUpdateChecks?: boolean;
    updateChannel?: string;
    detail?: string;
    pathIssue?: string;
  };
  type Bundle = { name: string; state: string; author?: string; description?: string; version?: string };
  type Job = {
    id: string;
    state: string;
    phase: string;
    bytes: number;
    error?: string;
  };
  let locale = $state<Locale>(
    (localStorage.getItem("replay.locale") ??
      (navigator.language.startsWith("de") ? "de" : "en")) as Locale,
  );
  let t = $derived(catalog(locale));
  let ready = $state(false),
    configured = $state(true),
    authenticated = $state(false),
    csrf = $state(""),
    setupConsole = $state<string[]>([]),
    pickerBusy = $state(false),
    pickerFailed = $state(false);
  let dashboardName = $state("Unofficial BlackICE - Server Webpanel"),
    username = $state("admin"),
    password = $state(""),
    confirmation = $state(""),
    key = $state("");
  let backend = $state("windows"),
    serverRoot = $state(""),
    mode = $state("download"),
    step = $state(0),
    validated = $state(false);
  let busy = $state(false),
    error = $state(""),
    notice = $state(""),
    connected = $state(false),
    view = $state("dashboard");
  let status = $state<Status | null>(null),
    lines = $state<string[]>([]),
    logs = $state<string[]>([]),
    command = $state(""),
    follow = $state(true),
    logView = $state(false);
  let logArchiveOpen = $state(false),
    showKey = $state(false),
    loginUser = $state(""),
    remember = $state(false),
    serverName = $state("");
  const mb = (n: number) => new Intl.NumberFormat(locale, { maximumFractionDigits: 1 }).format(n / 1048576) + " MB";
  async function loadServerName() {
    try {
      const data = await request("/api/server-name");
      serverName = data.name ?? "";
    } catch {
      serverName = "";
    }
  }
  let settingsTab = $state("general"),
    panelVersion = $state(""),
    serverBuild = $state(""),
    panelUpdate = $state(false),
    serverUpdate = $state(false);
  let settingsTabs = $derived(can("settings.write") ? ["general", "updates", "bridge", "server", "bundles"] : ["general"]);
  async function loadVersions() {
    if (!can("settings.write")) return;
    try {
      const auto = ((await request("/api/status")).status?.autoUpdateChecks ?? true) !== false;
      let p = await request("/api/updates");
      if (!p.latest && auto) p = await request("/api/updates/check", {});
      panelUpdate = !!p.available;
      let v = await request("/api/server-updates");
      if (!v.latest && auto) v = await request("/api/server-updates/check", {});
      serverBuild = v.current ?? "";
      serverUpdate = !!(v.current && v.latest && v.current !== v.latest);
    } catch {
      /* update info is optional */
    }
  }
  async function refreshVersions() {
    try {
      const boot = await request("/api/bootstrap");
      panelVersion = boot.version ?? panelVersion;
    } catch {
      /* keep previous */
    }
    await loadVersions();
  }
  function openUpdates() {
    settingsTab = "updates";
    navigate("settings");
  }
  let bundles = $state<Bundle[]>([]),
    bundleControl = $state(false),
    bundleError = $state(""),
    checked = $state(""),
    bundleLoading = $state(false);
  let job = $state<Job | null>(null);
  let dialog: HTMLDialogElement;
  let output = $state<HTMLTextAreaElement>();
  let pending = $state(""),
    stream: EventSource | undefined;
  let serverState = $derived(status?.state ?? "unknown");
  let text = $derived(
    (logView
      ? logs
      : lines.filter(
          (x) => !x.includes("panel-query-") && !/^\s*>*\s*bundles\s*$/.test(x),
        )
    ).join("\n") || (logView ? t.emptyLogs : t.emptyConsole),
  );
  let languageOptions = $derived([
    { value: "en", label: t.english },
    { value: "de", label: t.german },
  ]);
  function setLocale(value: string) {
    locale = value as Locale;
    localStorage.setItem("replay.locale", locale);
    document.documentElement.lang = locale;
    if (authenticated) run(async () => {
      const data = await request("/api/settings", { locale });
      status = data.status;
    });
  }
  function message(code: string) {
    return (
      t.errors[code as keyof typeof t.errors] ?? t.errors["request.failed"]
    );
  }
  async function request(url: string, body?: object) {
    let response: Response;
    try {
      response = await fetch(
        url,
        body === undefined
          ? undefined
          : {
              method: "POST",
              headers: { "Content-Type": "application/json", "X-CSRF": csrf },
              body: JSON.stringify(body),
            },
      );
    } catch {
      throw new Error("network.failed");
    }
    const data = await response.json().catch(() => ({}));
    if (!response.ok)
      throw new Error(
        response.status === 401
          ? "auth.failed"
          : response.status === 403 ? "auth.denied"
          : response.status === 429
            ? "auth.rate"
            : (data.error ?? "request.failed"),
      );
    return data;
  }
  async function run(fn: () => Promise<void>) {
    busy = true;
    error = "";
    notice = "";
    try {
      await fn();
    } catch (e) {
      error = (e as Error).message;
    } finally {
      busy = false;
    }
  }
  async function session() {
    const data = await request("/api/session");
    csrf = data.csrf;
    authenticated = data.authenticated;
    permissions = data.permissions ?? [];
    if (!can("logs.read")) { logs = []; logView = false; }
    if (!can("console.read")) lines = [];
    const boot = await request("/api/bootstrap");
    configured = boot.configured;
    panelVersion = boot.version ?? "";
    if (configured && authenticated) { loadVersions(); loadServerName(); }
    if (!configured && !serverRoot) serverRoot = boot.defaultServerRoot ?? "";
    dashboardName = boot.dashboardName;
    backend = boot.backend;
    if (!configured) {
      try { setupConsole = await request("/api/setup/console"); } catch { setupConsole = []; }
    } else setupConsole = [];
  }
  function connect() {
    stream?.close();
    stream = new EventSource("/api/events");
    stream.onmessage = (e) => {
      const data = JSON.parse(e.data);
      permissions = data.permissions ?? permissions;
      if (!can("logs.read")) { logs = []; logView = false; }
      if (!navItems.includes(view)) view = "dashboard";
      connected = data.status.ok;
      if (connected) status = data.status.status;
      else error = data.status.error;
      lines = data.console.lines ?? [];
    };
    stream.onerror = async () => { connected = false; try { await session(); if (!authenticated) stream?.close(); } catch {} };
  }
  async function login(e: SubmitEvent) {
    e.preventDefault();
    await run(async () => {
      await request("/api/login", { username: loginUser, password, remember });
      password = "";
      await session();
      connect();
    });
  }
  async function logout() {
    await run(async () => {
      await request("/api/logout", {});
      stream?.close();
      authenticated = false;
      permissions = []; logs = []; lines = []; logView = false; bundles = []; command = ""; view = "dashboard";
      status = null;
      await session();
    });
  }
  async function action(name: string) {
    await run(async () => {
      const data = await request("/api/server/" + name, {});
      status = data.status;
      notice = ({ start: "noticeStart", stop: "noticeStop", restart: "noticeRestart", "force-stop": "noticeForce" } as Record<string, string>)[name] ?? "actionDone";
    });
  }
  function confirm(name: string) {
    pending = name;
    dialog.showModal();
  }
  async function send(e: SubmitEvent) {
    e.preventDefault();
    await run(async () => {
      await request("/api/command", { text: command });
      command = "";
      notice = "sent";
    });
  }
  async function loadLogs() {
    await run(async () => {
      const data = await request("/api/logs");
      logs = data.lines;
      logView = true;
    });
  }
  function receive(data: {
    ok: boolean;
    bundles: Bundle[];
    canControl: boolean;
    checkedAt: string;
    error?: string;
  }) {
    bundleControl = data.ok && data.canControl;
    bundleError = data.error ?? "";
    if (data.ok) {
      bundles = data.bundles;
      checked = data.checkedAt;
    }
  }
  async function loadBundles() {
    if (bundleLoading || busy) return;
    bundleLoading = true;
    try {
      receive(await request("/api/bundles"));
    } catch (e) {
      bundleError = (e as Error).message;
      bundleControl = false;
    } finally {
      bundleLoading = false;
    }
  }
  async function bundleAction(bundle: Bundle, name: string) {
    await run(async () => {
      receive(
        await request(
          "/api/bundles/" + encodeURIComponent(bundle.name) + "/" + name,
          {},
        ),
      );
      if (bundleError) error = bundleError;
      else notice = (name === "start" ? t.bundleStarted : name === "stop" ? t.bundleStopped : t.bundleRestarted).replace("{name}", bundle.name);
    });
    if (!bundleControl) await loadBundles();
  }
  function navigate(name: string) {
    notice = "";
    error = "";
    view = name;
    if (name === "bundles") {
      loadBundles();
      loadBundleTools();
    }
  }
  async function settings(value: string) {
    await run(async () => {
      const data = await request("/api/settings", { shutdownMode: value });
      status = data.status;
      notice = "saved";
    });
  }
  async function autoStart(value: boolean) {
    await run(async () => {
      const data = await request("/api/settings", { autoStartServer: value });
      status = data.status;
      notice = "saved";
    });
  }
  async function updateChannel(value: string) {
    await run(async () => {
      const data = await request("/api/settings", { updateChannel: value });
      status = data.status;
      notice = "saved";
    });
    try {
      await request("/api/updates/check", {});
    } catch {
      /* the update panel shows its own error on the next poll */
    }
    await refreshVersions();
  }
  async function updateChecks(value: boolean) {
    await run(async () => {
      const data = await request("/api/settings", { autoUpdateChecks: value });
      status = data.status;
      notice = "saved";
    });
  }
  let btState = $state<any>(null),
    bundleView = $state("list");
  async function loadBundleTools() {
    try {
      btState = await request("/api/bundle-tools");
      if (!btState.enabled) bundleView = "list";
    } catch {
      btState = null;
      bundleView = "list";
    }
  }
  function uploadBundle(file: File, progress: (percent: number) => void): Promise<any> {
    return new Promise((resolve, reject) => {
      const form = new FormData();
      form.append("file", file, file.name);
      const xhr = new XMLHttpRequest();
      xhr.open("POST", "/api/bundle-tools/upload");
      xhr.setRequestHeader("X-CSRF", csrf);
      xhr.upload.onprogress = (e) => {
        if (e.lengthComputable) progress(Math.round((e.loaded * 100) / e.total));
      };
      xhr.onload = () => {
        let body: any = null;
        try {
          body = JSON.parse(xhr.responseText);
        } catch {
          /* non-JSON error body */
        }
        if (xhr.status >= 200 && xhr.status < 300) resolve(body);
        else reject(new Error(body?.error ?? "request.failed"));
      };
      xhr.onerror = () => reject(new Error("network.failed"));
      xhr.send(form);
    });
  }
  async function pick() {
    pickerBusy = true;
    try {
      await request("/api/setup/pick", { key, path: serverRoot });
      for (;;) {
        await new Promise((r) => setTimeout(r, 800));
        const r = await request("/api/setup/pick-status", { key });
        if (r.state === "running") continue;
        if (r.state === "done") {
          serverRoot = r.path;
          validated = false;
        } else if (r.state === "unavailable") pickerFailed = true;
        break;
      }
    } catch {
      pickerFailed = true;
    } finally {
      pickerBusy = false;
    }
  }
  async function validate() {
    await run(async () => {
      await request("/api/setup/validate", { key, serverRoot, backend });
      validated = true;
      notice = "valid";
    });
  }
  async function install() {
    validated = false;
    await run(async () => {
      job = await request("/api/setup/install", { key, serverRoot, backend });
    });
  }
  async function finish(e: SubmitEvent) {
    e.preventDefault();
    await run(async () => {
      await request("/api/setup/finish", {
        key,
        dashboardName,
        username,
        password,
        confirmation,
        locale,
        serverRoot,
        backend,
      });
      await request("/api/login", { username, password, remember: false });
      password = "";
      confirmation = "";
      key = "";
      await session();
      connect();
      notice = "setupSuccess";
    });
  }
  function tabs(e: KeyboardEvent) {
    const names = navItems;
    let n = names.indexOf(view);
    if (e.key === "ArrowRight") n = (n + 1) % names.length;
    else if (e.key === "ArrowLeft") n = (n + names.length - 1) % names.length;
    else if (e.key === "Home") n = 0;
    else if (e.key === "End") n = names.length - 1;
    else return;
    e.preventDefault();
    navigate(names[n]);
    e.currentTarget &&
      (e.currentTarget as HTMLElement)
        .querySelectorAll<HTMLButtonElement>("button")
        [n]?.focus();
  }
  function phase(value: string) {
    return value === "download"
      ? t.downloadPhase
      : value === "extract"
        ? t.extract
        : value === "verify"
          ? t.verify
          : value === "completed"
            ? t.completed
            : value === "cancelled"
              ? t.cancelled
              : value === "failed"
                ? t.failed
                : t.idle;
  }
  onMount(() => {
    setLocale(locale);
    session()
      .then(() => {
        if (authenticated) connect();
      })
      .catch((e) => (error = e.message))
      .finally(() => (ready = true));
    const timer = setInterval(async () => {
      if (job?.state === "running") {
        try {
          job = await request("/api/setup/job", { key });
          if (job?.state === "completed") validated = true;
          if (job?.error) error = job.error;
        } catch (e) {
          error = (e as Error).message;
        }
      }
    }, 1500);
    return () => {
      stream?.close();
      clearInterval(timer);
    };
  });
  $effect(() => {
    const content = text;
    if (follow && output && !logView)
      requestAnimationFrame(() => {
        if (output) output.scrollTop = output.scrollHeight;
      });
    void content;
  });
</script>

<svelte:head
  ><title>{dashboardName}</title><meta
    name="theme-color"
    content="#101116"
  /></svelte:head
>
<HexBackground />
<header class="toolbar">
  <a href="/" class="brand" aria-label={t.app}
    ><svg viewBox="0 0 28 28" aria-hidden="true"
      ><path d="M5 23V5h11l7 7-7 7H5m9 0 8 4M5 12h11" /></svg
    ><span class="brand-text"><span>{t.app}</span>{#if authenticated && serverName}<small>{serverName}</small>{/if}</span></a
  >
  <div class="header-server">      {#if authenticated}
        {#if status?.pathIssue}
        <div class="server-path-warning"><span role="alert">{status.pathIssue==='serverPath.missing'?t.pathMissing:t.pathFiles}</span>{#if can("settings.write")}<button onclick={()=>{settingsTab="general";navigate("settings");}}>{t.pathEdit}</button>{/if}{#if serverState==="running"&&can("server.stop")}<button class="danger" onclick={()=>confirm("stop")}>{t.stop}</button>{/if}</div>
        {:else}
        <div
          class="actions header-actions"
          role="group"
          aria-label={t.serverActions}
        >
          <button
            class="primary"
            onclick={() => action("start")}
            disabled={busy || !connected || !can("server.start") || serverState !== "stopped"}
            >{t.start}</button
          ><button
            onclick={() => confirm("restart")}
            disabled={busy || !connected || !can("server.restart") || serverState !== "running"}
            >{t.restart}</button
          ><button
            class="danger"
            onclick={() => confirm("stop")}
            disabled={busy || !connected || !can("server.stop") || serverState !== "running"}
            >{t.stop}</button
          >
        </div>        <span
          class="state-badge"
          class:online={connected && serverState === "running"}
          >{connected
            ? t[
                serverState as
                  | "running"
                  | "stopped"
                  | "unknown"
                  | "external"
                  | "unconfigured"
              ]
            : t.disconnected}</span
        >

        {/if}
      {/if}
</div>
  <div class="toolbar-right">
    {#if authenticated}<span class="connection" class:online={connected}
        >{connected ? t.connected : t.disconnected}</span
      >{/if}<Select
      label={t.language}
      value={locale}
      options={languageOptions}
      onchange={setLocale}
    />{#if authenticated}<button class="quiet" onclick={logout}
        >{t.logout}</button
      >{/if}
  </div>
</header>
{#if !ready}<main class="auth">
    <p role="status">{t.loading}</p>
    {#if error}<p class="error" role="alert">{message(error)}</p>{/if}
  </main>
{:else if !configured}
  <main class="auth setup">
    <h1>{t.welcome}</h1>
    <p class="muted">{t.setupIntro}</p>
    <textarea
      class="console-output setup-console"
      readonly
      rows="2"
      aria-label={t.setupConsole}
      value={setupConsole.join("\n")}
    ></textarea>
    <ol class="steps" aria-label={t.setup}>
      {#each [t.account, t.server, t.finish] as name, index}<li
          class:current={step === index}
          aria-current={step === index ? "step" : undefined}
        >
          <span>{index + 1}</span>{name}
        </li>{/each}
    </ol>
    <form onsubmit={finish}>
      {#if step === 0}
        <div class="fields">
          <div class="field key-field">
            <label
              >{t.setupKey}<input
                type={showKey ? "text" : "password"}
                bind:value={key}
                autocomplete="off"
                spellcheck="false"
                required
              /></label
            ><button type="button" class="reveal" onclick={() => (showKey = !showKey)}
              >{showKey ? t.setupKeyHide : t.setupKeyReveal}</button
            >
          </div>
          <label
            >{t.dashboardName}<input
              bind:value={dashboardName}
              maxlength="80"
              required
            /></label
          >
          <label
            >{t.username}<input
              bind:value={username}
              autocomplete="username"
              minlength="3"
              maxlength="32"
              pattern="[A-Za-z0-9_.-]+"
              required
            /></label
          >
          <label
            >{t.password}<input
              type="password"
              bind:value={password}
              autocomplete="new-password"
              minlength="8"
              maxlength="256"
              required
            /><small>{t.setupPasswordHint}</small></label
          >
          <label
            >{t.confirmation}<input
              type="password"
              bind:value={confirmation}
              autocomplete="new-password"
              minlength="8"
              required
            /></label
          >
        </div>
        <button
          type="button"
          class="primary"
          disabled={!key ||
            !dashboardName ||
            username.length < 3 ||
            password.length < 8 ||
            password !== confirmation}
          onclick={() => (step = 1)}>{t.next}</button
        >
      {:else if step === 1}
        <div class="fields">
          <div class="field">
            <span>{t.backend}</span><Select
              label={t.backend}
              value={backend}
              options={backend === "windows"
                ? [{ value: "windows", label: t.windows }]
                : [
                    { value: "linux", label: t.linux },
                    { value: "docker", label: t.docker },
                  ]}
              onchange={(value) => {
                backend = value;
                validated = false;
              }}
            />
          </div>
          <div class="field">
            <span>{t.mode}</span><Select
              label={t.mode}
              value={mode}
              options={[
                { value: "existing", label: t.existing },
                { value: "download", label: t.download },
              ]}
              onchange={(value) => {
                mode = value;
                validated = false;
              }}
            />
          </div>
          <label
            >{t.serverRoot}<input
              bind:value={serverRoot}
              oninput={() => (validated = false)}
              readonly={!pickerFailed}
              required
            /><small>{pickerFailed ? t.directPath : t.serverRootHint}</small></label
          >
        </div>
        <div class="actions">
          {#if !pickerFailed}<button
              type="button"
              onclick={pick}
              disabled={busy || pickerBusy || !key}
              >{pickerBusy ? t.pickerOpen : t.changeFolder}</button
            >{/if}<button
            type="button"
            onclick={mode === "existing" ? validate : install}
            disabled={busy || !serverRoot || job?.state === "running"}
            >{mode === "existing" ? t.validate : t.install}</button
          >
        </div>
        {#if job}<section class="installation" aria-label={t.progress}>
            <p role="status">
              {phase(job.state === "running" ? job.phase : job.state)}
            </p>
            <div
              role="progressbar"
              aria-label={t.progress}
              aria-valuetext={phase(job.phase)}
              class="progress"
              class:indeterminate={job.state === "running"}
            >
              <span></span>
            </div>
            <p class="muted">
              {t.bytes}: {mb(job.bytes)}
            </p>
            {#if job.state === "running"}<button
                type="button"
                onclick={() =>
                  run(async () => {
                    await request("/api/setup/cancel", { key });
                  })}>{t.cancel}</button
              >{/if}
          </section>{/if}
        <div class="actions">
          <button type="button" onclick={() => (step = 0)}>{t.back}</button
          ><button
            type="button"
            class="primary"
            disabled={!validated || busy}
            onclick={() => (step = 2)}>{t.next}</button
          >
        </div>
      {:else}
        <dl>
          <dt>{t.dashboardName}</dt>
          <dd>{dashboardName}</dd>
          <dt>{t.username}</dt>
          <dd>{username}</dd>
          <dt>{t.backend}</dt>
          <dd>{backend}</dd>
          <dt>{t.serverRoot}</dt>
          <dd>{serverRoot}</dd>
        </dl>
        <div class="actions">
          <button type="button" onclick={() => (step = 1)}>{t.back}</button
          ><button class="primary" disabled={busy || !validated}
            >{busy ? t.loading : t.setup}</button
          >
        </div>
      {/if}
    </form>
    {#if error}<p role="alert" class="error">
        {message(error)}
      </p>{/if}{#if notice}<p role="status" class="notice">
        {t[notice as keyof typeof t] as string}
      </p>{/if}
  </main>
{:else if !authenticated}
  <main class="auth">
    <h1>{t.login}</h1>
    <p class="muted">{t.loginHint}</p>
    <form onsubmit={login} class="fields">
      <label
        >{t.username}<input
          bind:value={loginUser}
          autocomplete="username"
          required
        /></label
      ><label
        >{t.password}<input
          type="password"
          bind:value={password}
          autocomplete="current-password"
          maxlength="256"
          required
        /></label
      ><Toggle checked={remember} label={t.stayLoggedIn} onchange={(v) => (remember = v)} /><button class="primary" disabled={busy}
        >{busy ? t.loading : t.login}</button
      >
    </form>
    {#if error}<p role="alert" class="error">
        {message(error)}
      </p>{/if}{#if notice}<p class="notice" role="status">
        {t[notice as keyof typeof t] as string}
      </p>{/if}
  </main>
{:else}
  <div class="app-shell">
    <nav class="navigation" aria-label={t.navigation}>
      <div
        role="tablist"
        aria-label={t.navigation}
        tabindex="-1"
        onkeydown={tabs}
      >
        {#each navItems as name, index}<button
            id={"tab-" + name}
            role="tab"
            aria-selected={view === name}
            aria-controls={"view-" + name}
            tabindex={view === name ? 0 : -1}
            class:active={view === name}
            onclick={() => navigate(name)}
            ><svg viewBox="0 0 24 24" aria-hidden="true"
              >{#if name === "dashboard"}<path
                  d="M3 3h7v7H3zm11 0h7v7h-7zM3 14h7v7H3zm11 0h7v7h-7z"
                />{:else if name === "console"}<path
                  d="m4 6 6 6-6 6m9 0h7"
                />{:else if name === "bundles"}<path
                  d="m12 3 9 5-9 5-9-5zm-9 9 9 5 9-5m-18 5 9 5 9-5"
                />{:else if name === "users"}<path
                  d="M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM4 21a8 8 0 0 1 16 0"
                />{:else}<path
                  d="M3 6h18M3 12h18M3 18h18M8 3v6m8 0v6m-7 0v6"
                />{/if}</svg
            >{t[
              name as "dashboard" | "console" | "bundles" | "settings" | "users"
            ]}</button
          >{/each}
      </div>
      <div class="nav-foot"><strong>BlackICE</strong><span>{t.unofficial}</span>{#if panelVersion}<button type="button" class="build" onclick={openUpdates} title={panelUpdate ? t.updateBadge : ""}>{t.panelBuild} {panelVersion}{#if panelVersion.includes("-")}<span class="exp-tag">{t.experimental}</span>{/if}{#if panelUpdate}<i class="dot" aria-label={t.updateBadge}></i>{/if}</button>{/if}{#if serverBuild}<button type="button" class="build" onclick={openUpdates} title={serverUpdate ? t.updateBadge : ""}>{t.serverBuild} {serverBuild.slice(0, 8)}{#if serverUpdate}<i class="dot" aria-label={t.updateBadge}></i>{/if}</button>{/if}<span class="links"><a href="https://github.com/LandminenTester/blackice-server-panel" target="_blank" rel="noopener noreferrer">GitHub ↗</a><a href="https://discord.gg/k6ttXWdAyP" target="_blank" rel="noopener noreferrer">Discord ↗</a></span></div>
    </nav>
    <div
      class="workspace"
      class:console-page={view === "console"}
      tabindex="-1"
      id={"view-" + view}
      role="tabpanel"
      aria-labelledby={"tab-" + view}
    >
      <section class="page-heading">
        <div>
          <h1>{t[view as "dashboard" | "console" | "bundles" | "settings" | "users"]}</h1>
          <p class="muted">
            {view === "dashboard"
              ? t.overviewHint
              : view === "console"
                ? t.consoleHint
                : view === "bundles"
                  ? t.bundlesHint
                  : view === "users" ? t.usersHint : t.settingsHint}
          </p>
        </div>
      </section>
      {#if error}<p class="error banner" role="alert">
          {message(error)}
        </p>{/if}{#if notice}{#key notice}<p class="toast" role="status" onanimationend={() => (notice = "")}>
          {(t as Record<string, any>)[notice] ?? notice}
        </p>{/key}{/if}
      {#if view === "dashboard"}
        <div class="dashboard-grid">
          <section class="runtime">
            <h2>{t.status}</h2>
            <dl>
              <dt>{t.pid}</dt>
              <dd>{status?.pid ?? "—"}</dd>
              <dt>{t.backend}</dt>
              <dd>{t[backend as "windows" | "linux" | "docker"]}</dd>
              <dt>{t.serverRoot}</dt>
              <dd class="path">{status?.serverRoot ?? "—"}</dd>
            </dl>
            <Bridge {t} {locale} {request} />
            {#if status?.detail}<p class="muted">
                {message(status.detail)}
              </p>{/if}
          </section>
          <section class="activity">
            <div class="section-heading">
              <h2>{t.latestOutput}</h2>
              <button disabled={!can("console.read") && !can("console.write") && !can("logs.read")} onclick={() => navigate("console")}
                >{t.openConsole}</button
              >
            </div>
            <textarea
              readonly
              class="activity-output"
              aria-label={t.latestOutput}
              value={lines
                .filter((x) => !x.includes("panel-query-"))
                .slice(-16)
                .join("\n") || t.emptyConsole}
            ></textarea>
          </section>
        </div>
      {:else if view === "console"}
        <section class="console">
          <DiagnosticStream {t} {locale} {request} consoleAllowed={can("console.read")} logsAllowed={can("logs.read")} bind:archive={logArchiveOpen} />
          {#if !logArchiveOpen}<form class="command-form" onsubmit={send}>
            <label class="sr-only" for="command">{t.command}</label><svg
              viewBox="0 0 16 16"
              aria-hidden="true"><path d="m4 3 5 5-5 5" /></svg
            ><input
              id="command"
              bind:value={command}
              placeholder={t.commandPlaceholder}
              maxlength="1024"
              autocomplete="off"
              disabled={busy || !connected || !can("console.write") || serverState !== "running"}
            /><button
              disabled={busy ||
                !connected ||
                serverState !== "running" ||
                !can("console.write") || !command.trim()}>{t.send}</button
            >
          </form>{/if}
        </section>
      {:else if view === "bundles"}
        {#if btState?.enabled && (can("bundles.upload") || can("bundles.build"))}
          <div class="subtabs" role="tablist" aria-label={t.bundles}>
            <button role="tab" aria-selected={bundleView === "list"} class:active={bundleView === "list"} onclick={() => (bundleView = "list")}>{t.bvList}</button>
            {#if btState.settings.sourcesEnabled && can("bundles.build")}<button role="tab" aria-selected={bundleView === "projects"} class:active={bundleView === "projects"} onclick={() => (bundleView = "projects")}>{t.bvProjects}<span class="exp-tag">{t.experimental}</span></button>{/if}
            {#if btState.settings.uploadEnabled && can("bundles.upload")}<button role="tab" aria-selected={bundleView === "upload"} class:active={bundleView === "upload"} onclick={() => (bundleView = "upload")}>{t.bvUpload}<span class="exp-tag">{t.experimental}</span></button>{/if}
          </div>
        {/if}
        {#if bundleView === "projects" && btState?.enabled}
          <BundleProjects {t} {locale} {request} tools={btState.toolchain} onChanged={loadBundles} />
        {:else if bundleView === "upload" && btState?.enabled}
          <BundleUpload {t} {locale} {request} upload={uploadBundle} maxMb={btState.uploadMaxMb} onInstalled={loadBundles} />
        {:else}
        <section class="bundle-panel" aria-busy={bundleLoading}>
          <div class="section-heading">
            <h2>{t.bundleStatus}</h2>
            <button onclick={loadBundles} disabled={busy || bundleLoading}
              >{bundleLoading ? t.loading : t.refresh}</button
            >
          </div>
          {#if checked}<p class="muted">
              {t.checked}: {new Date(checked).toLocaleString(locale)}
            </p>{/if}{#if bundleError}<p class="error" role="status">
              {message(bundleError)}
            </p>{/if}{#if !bundleLoading && !bundles.length}<p class="empty">
              {t.emptyBundles}
            </p>{/if}
          <ul class="bundle-list">
            {#each bundles as bundle (bundle.name)}<li>
                <div>
                  <strong>{bundle.name}</strong><span
                    class="bundle-state"
                    class:online={connected &&
                      bundleControl &&
                      bundle.state === "running"}
                    >{t[
                      (!connected
                        ? "unknown"
                        : serverState === "stopped"
                          ? "stopped"
                          : !bundleControl
                            ? "unknown"
                            : bundle.state) as "running" | "stopped" | "unknown"
                    ]}</span
                  >
                  {#if bundle.version}<small class="bundle-meta">v{bundle.version}</small>{/if}{#if bundle.author}<small class="bundle-meta">{t.bundleBy} {bundle.author}</small>{/if}
                  {#if bundle.description}<p class="muted bundle-desc">{bundle.description}</p>{/if}
                </div>
                <div
                  class="actions"
                  role="group"
                  aria-label={t.actions + " · " + bundle.name}
                >
                  {#if bundle.state === "running"}<button
                      onclick={() => bundleAction(bundle, "restart")}
                      disabled={busy || !can("bundles.restart") ||
                        bundleLoading ||
                        !bundleControl ||
                        !connected}>{t.restart}</button
                    ><button
                      class="danger"
                      onclick={() => bundleAction(bundle, "stop")}
                      disabled={busy || !can("bundles.stop") ||
                        bundleLoading ||
                        !bundleControl ||
                        !connected}>{t.stop}</button
                    >{:else}<button
                      class="primary"
                      onclick={() => bundleAction(bundle, "start")}
                      disabled={busy || !can("bundles.start") ||
                        bundleLoading ||
                        !bundleControl ||
                        !connected ||
                        bundle.state !== "stopped"}>{t.start}</button
                    >{/if}
                </div>
              </li>{/each}
          </ul>
          {#if busy}<p role="status">{t.actionPending}</p>{/if}
        </section>
        {/if}
      {:else if view === "users" && can("users.manage")}
        <Users {t} {request} onchanged={session} />
      {:else}
        <section class="settings-section">
          <div class="subtabs" role="tablist" aria-label={t.settings}>
            {#each settingsTabs as name}<button
                role="tab"
                id={"stab-" + name}
                aria-selected={settingsTab === name}
                class:active={settingsTab === name}
                onclick={() => (settingsTab = name)}
                >{t[("set" + name[0].toUpperCase() + name.slice(1)) as "setGeneral"]}{#if name === "bundles"}<span class="exp-tag">{t.experimental}</span>{/if}{#if name === "updates" && (panelUpdate || serverUpdate)}<i class="dot" aria-label={t.updateBadge}></i>{/if}</button
              >{/each}
          </div>
          {#if settingsTab === "updates" && can("settings.write")}
            <Updates {t} {request} installAllowed={can("users.manage")} onchanged={refreshVersions} autoCheck={status?.autoUpdateChecks !== false} channel={status?.updateChannel ?? "stable"} onchannel={updateChannel} />
            <ServerUpdates {t} {locale} {request} installAllowed={can("users.manage")} onchanged={refreshVersions} />
          {:else if settingsTab === "bridge" && can("settings.write")}
            <Bridge {t} {locale} {request} settings {backend} />
          {:else if settingsTab === "bundles" && can("settings.write")}
            <BundleToolsSettings {t} {request} onchanged={loadBundleTools} />
          {:else if settingsTab === "server" && can("settings.write")}
            <ServerConfig {t} {locale} {request} {backend} onsaved={loadServerName} />
          {:else}
          {#if can("settings.write")}<ServerPath {t} {request} serverRoot={status?.serverRoot??""} {serverState} {backend} {connected} onchanged={value=>{status=value;serverName="";loadServerName();}}/>{/if}
          <section class="update-panel general-panel">
          <h2>{t.autoStart}</h2>
          <Toggle checked={status?.autoStartServer ?? true} label={t.autoStart} disabled={busy || !connected || !can("settings.write")} onchange={autoStart} />
          <p class="muted">{t.autoStartHint}</p>
          </section>
          {#if can("settings.write")}<section class="update-panel general-panel">
          <h2>{t.autoUpdateChecks}</h2>
          <Toggle checked={status?.autoUpdateChecks ?? true} label={t.autoUpdateChecks} disabled={busy || !connected} onchange={updateChecks} />
          <p class="muted">{t.autoUpdateChecksHint}</p>
          </section>{/if}
          <section class="update-panel general-panel">
          <h2>{t.shutdown}</h2>
          <Select
            value={status?.shutdownMode ?? "keep-running"}
            label={t.shutdown}
            disabled={busy || !connected || !can("settings.write")}
            options={[
              { value: "keep-running", label: t.keepRunning },
              { value: "stop-server", label: t.stopServer },
            ]}
            onchange={settings}
          />
          <p class="muted">{t.shutdownHint}</p>
          </section>
          <section class="update-panel general-panel">
          <h2>{t.language}</h2>
          <Select
            value={locale}
            label={t.language}
            options={languageOptions}
            onchange={setLocale}
          />
          </section>
          <section class="update-panel general-panel force-panel">
          <div><h2>{t.forceStop}</h2>
          <p class="muted">{t.forceHint}</p></div>
          <button
            class="danger"
            onclick={() => confirm("force-stop")}
            disabled={busy || !connected || !can("server.force-stop") || serverState !== "running"}
            >{t.forceStop}</button
          >
          </section>
          {/if}
        </section>
      {/if}

    </div>
  </div>
{/if}
<dialog bind:this={dialog} aria-labelledby="confirm-title">
  <h2 id="confirm-title">{t.confirmTitle}</h2>
  <p>{pending === "force-stop" ? t.forceBody : t.confirmBody}</p>
  <div class="actions">
    <button onclick={() => dialog.close()}>{t.cancel}</button><button
      class="danger"
      onclick={() => {
        dialog.close();
        action(pending);
      }}>{t.confirm}</button
    >
  </div>
</dialog>
