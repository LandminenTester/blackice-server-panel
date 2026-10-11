<script lang="ts">
  import { onMount } from "svelte";
  import { catalog, type Locale } from "./locales";
  import Select from "./components/Select.svelte";
  import Toggle from "./components/Toggle.svelte";
  type Status = {
    state: string;
    pid: number | null;
    serverRoot: string;
    shutdownMode: string;
    autoStartServer: boolean;
    detail?: string;
  };
  type Bundle = { name: string; state: string };
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
    csrf = $state("");
  let dashboardName = $state("Unofficial REPLAY.re - Server Webpanel"),
    username = $state("admin"),
    password = $state(""),
    confirmation = $state(""),
    key = $state("");
  let backend = $state("windows"),
    serverRoot = $state(""),
    mode = $state("existing"),
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
  let bundles = $state<Bundle[]>([]),
    bundleControl = $state(false),
    bundleError = $state(""),
    checked = $state(""),
    bundleLoading = $state(false);
  let job = $state<Job | null>(null),
    directories = $state<string[]>([]),
    folder = $state(""),
    parent = $state<string | null>(null);
  let dialog: HTMLDialogElement;
  let folderDialog: HTMLDialogElement;
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
    const boot = await request("/api/bootstrap");
    configured = boot.configured;
    dashboardName = boot.dashboardName;
    backend = boot.backend;
  }
  function connect() {
    stream?.close();
    stream = new EventSource("/api/events");
    stream.onmessage = (e) => {
      const data = JSON.parse(e.data);
      connected = data.status.ok;
      if (connected) status = data.status.status;
      else error = data.status.error;
      lines = data.console.lines ?? [];
    };
    stream.onerror = () => (connected = false);
  }
  async function login(e: SubmitEvent) {
    e.preventDefault();
    await run(async () => {
      await request("/api/login", { username, password });
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
      status = null;
      await session();
    });
  }
  async function action(name: string) {
    await run(async () => {
      const data = await request("/api/server/" + name, {});
      status = data.status;
      notice = "actionDone";
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
      else notice = "actionDone";
    });
    if (!bundleControl) await loadBundles();
  }
  function navigate(name: string) {
    notice = "";
    error = "";
    view = name;
    if (name === "bundles") loadBundles();
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
  async function folders(path: string) {
    await run(async () => {
      const data = await request("/api/setup/folders", { key, path });
      directories = data.directories;
      folder = data.path;
      parent = data.parent;
    });
  }
  async function browse() {
    await folders(serverRoot);
    folderDialog.showModal();
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
      password = "";
      confirmation = "";
      key = "";
      await session();
      notice = "setupSuccess";
    });
  }
  function tabs(e: KeyboardEvent) {
    const names = ["dashboard", "console", "bundles", "settings"];
    let n = names.indexOf(view);
    if (e.key === "ArrowRight") n = (n + 1) % 4;
    else if (e.key === "ArrowLeft") n = (n + 3) % 4;
    else if (e.key === "Home") n = 0;
    else if (e.key === "End") n = 3;
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
      if (authenticated && view === "bundles") loadBundles();
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
<header class="toolbar">
  <a href="/" class="brand"
    ><svg viewBox="0 0 28 28" aria-hidden="true"
      ><path d="M5 23V5h11l7 7-7 7H5m9 0 8 4M5 12h11" /></svg
    ><span>{dashboardName}</span></a
  >
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
          <label
            >{t.setupKey}<input
              bind:value={key}
              autocomplete="off"
              required
            /><small>{t.setupKeyHint}</small></label
          >
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
              minlength="12"
              maxlength="256"
              required
            /><small>{t.setupPasswordHint}</small></label
          >
          <label
            >{t.confirmation}<input
              type="password"
              bind:value={confirmation}
              autocomplete="new-password"
              minlength="12"
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
            password.length < 12 ||
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
              placeholder={backend === "windows"
                ? "C:Replay Server"
                : "/srv/replay"}
              required
            /><small>{t.directPath}</small></label
          >
        </div>
        <div class="actions">
          <button type="button" onclick={browse} disabled={busy || !key}
            >{t.browse}</button
          ><button
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
              {t.bytes}: {new Intl.NumberFormat(locale).format(job.bytes)} B
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
          bind:value={username}
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
      ><button class="primary" disabled={busy}
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
        {#each ["dashboard", "console", "bundles", "settings"] as name, index}<button
            id={"tab-" + name}
            role="tab"
            aria-selected={view === name}
            aria-controls={"view-" + name}
            tabindex={view === name ? 0 : -1}
            class:active={view === name}
            onclick={() => navigate(name)}
            ><svg viewBox="0 0 24 24" aria-hidden="true"
              >{#if index === 0}<path
                  d="M3 3h7v7H3zm11 0h7v7h-7zM3 14h7v7H3zm11 0h7v7h-7z"
                />{:else if index === 1}<path
                  d="m4 6 6 6-6 6m9 0h7"
                />{:else if index === 2}<path
                  d="m12 3 9 5-9 5-9-5zm-9 9 9 5 9-5m-18 5 9 5 9-5"
                />{:else}<path
                  d="M3 6h18M3 12h18M3 18h18M8 3v6m8 0v6m-7 0v6"
                />{/if}</svg
            >{t[
              name as "dashboard" | "console" | "bundles" | "settings"
            ]}</button
          >{/each}
      </div>
      <p class="nav-foot">Replay</p>
    </nav>
    <div
      class="workspace"
      tabindex="-1"
      id={"view-" + view}
      role="tabpanel"
      aria-labelledby={"tab-" + view}
    >
      <section class="page-heading">
        <div>
          <h1>{t[view as "dashboard" | "console" | "bundles" | "settings"]}</h1>
          <p class="muted">
            {view === "dashboard"
              ? t.overviewHint
              : view === "console"
                ? t.consoleHint
                : view === "bundles"
                  ? t.bundlesHint
                  : t.settingsHint}
          </p>
        </div>
        <span
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
      </section>
      {#if error}<p class="error banner" role="alert">
          {message(error)}
        </p>{/if}{#if notice}<p class="notice" role="status">
          {t[notice as keyof typeof t] as string}
        </p>{/if}
      {#if view === "dashboard" || view === "console"}
        <div
          class="actions server-actions"
          role="group"
          aria-label={t.serverActions}
        >
          <button
            class="primary"
            onclick={() => action("start")}
            disabled={busy || !connected || serverState !== "stopped"}
            >{t.start}</button
          ><button
            onclick={() => confirm("restart")}
            disabled={busy || !connected || serverState !== "running"}
            >{t.restart}</button
          ><button
            class="danger"
            onclick={() => confirm("stop")}
            disabled={busy || !connected || serverState !== "running"}
            >{t.stop}</button
          >
        </div>
      {/if}
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
            {#if status?.detail}<p class="muted">
                {message(status.detail)}
              </p>{/if}
          </section>
          <section class="activity">
            <div class="section-heading">
              <h2>{t.latestOutput}</h2>
              <button onclick={() => navigate("console")}
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
          <div class="console-toolbar">
            <div class="segmented" role="group" aria-label={t.console}>
              <button
                aria-pressed={!logView}
                class:active={!logView}
                onclick={() => (logView = false)}>{t.live}</button
              ><button
                aria-pressed={logView}
                class:active={logView}
                onclick={loadLogs}>{t.logs}</button
              >
            </div>
            {#if logView}<button onclick={loadLogs}>{t.refresh}</button
              >{:else}<Toggle
                checked={follow}
                label={t.follow}
                onchange={(value) => (follow = value)}
              />{/if}
          </div>
          <textarea
            class="console-output"
            bind:this={output}
            readonly
            aria-label={logView ? t.logs : t.console}
            value={text}
          ></textarea>
          <form class="command-form" onsubmit={send}>
            <label class="sr-only" for="command">{t.command}</label><svg
              viewBox="0 0 16 16"
              aria-hidden="true"><path d="m4 3 5 5-5 5" /></svg
            ><input
              id="command"
              bind:value={command}
              placeholder={t.commandPlaceholder}
              maxlength="1024"
              autocomplete="off"
              disabled={busy || !connected || serverState !== "running"}
            /><button
              disabled={busy ||
                !connected ||
                serverState !== "running" ||
                !command.trim()}>{t.send}</button
            >
          </form>
        </section>
      {:else if view === "bundles"}
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
                </div>
                <div
                  class="actions"
                  role="group"
                  aria-label={t.actions + " · " + bundle.name}
                >
                  {#if bundle.state === "running"}<button
                      onclick={() => bundleAction(bundle, "restart")}
                      disabled={busy ||
                        bundleLoading ||
                        !bundleControl ||
                        !connected}>{t.restart}</button
                    ><button
                      class="danger"
                      onclick={() => bundleAction(bundle, "stop")}
                      disabled={busy ||
                        bundleLoading ||
                        !bundleControl ||
                        !connected}>{t.stop}</button
                    >{:else}<button
                      class="primary"
                      onclick={() => bundleAction(bundle, "start")}
                      disabled={busy ||
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
      {:else}
        <section class="settings-section">
          <h2>{t.autoStart}</h2>
          <Toggle checked={status?.autoStartServer ?? true} label={t.autoStart} disabled={busy || !connected} onchange={autoStart} />
          <p class="muted">{t.autoStartHint}</p>
          <h2>{t.shutdown}</h2>
          <Select
            value={status?.shutdownMode ?? "keep-running"}
            label={t.shutdown}
            disabled={busy || !connected}
            options={[
              { value: "keep-running", label: t.keepRunning },
              { value: "stop-server", label: t.stopServer },
            ]}
            onchange={settings}
          />
          <p class="muted">{t.shutdownHint}</p>
          <h2>{t.language}</h2>
          <Select
            value={locale}
            label={t.language}
            options={languageOptions}
            onchange={setLocale}
          />
          <h2>{t.forceStop}</h2>
          <p class="muted">{t.forceHint}</p>
          <button
            class="danger"
            onclick={() => confirm("force-stop")}
            disabled={busy || !connected || serverState !== "running"}
            >{t.forceStop}</button
          >
        </section>
      {/if}
      <footer>Unofficial REPLAY.re - Server Webpanel <span>{t.backend}: {backend}</span></footer>
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
<dialog
  class="folder-dialog"
  bind:this={folderDialog}
  aria-labelledby="folder-title"
>
  <h2 id="folder-title">{t.browse}</h2>
  <p class="muted">{t.directoryHint}</p>
  <p class="path">{folder || "/"}</p>
  <div class="folder-list">
    {#if parent}<button onclick={() => folders(parent!)}>{t.parent}</button
      >{/if}{#each directories as directory}<button
        onclick={() => folders(directory)}>{directory}</button
      >{/each}
  </div>
  <div class="actions">
    <button onclick={() => folderDialog.close()}>{t.cancel}</button><button
      class="primary"
      disabled={!folder}
      onclick={() => {
        serverRoot = folder;
        validated = false;
        folderDialog.close();
      }}>{t.chooseFolder}</button
    >
  </div>
  {#if error}<p class="error" role="alert">{message(error)}</p>{/if}
</dialog>
