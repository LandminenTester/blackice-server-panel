# LAN bridge and diagnostics / LAN-Bridge und Diagnose

## English

The bridge is optional and off on existing installations. It implements the server-side JSON-line protocol from blackice-mcp directly in the .NET Supervisor; no Python or AI runtime is required. Public servers continue to use PTY.

1. Use current official REServer files supporting `--mcp`, `--mcp-addr` and `--mcp-token`. Older files can ignore these flags and stay in PTY fallback.
2. In Settings expand **LAN bridge**, enable it and choose a free internal port (default 20774). Save and confirm the LAN implication: **--mcp forces sv_lan 1 and removes the server from the public list**.
3. Explicitly restart the server. Saving does not restart or rewrite cfg/server.cfg. Server start parameters are managed by the panel; do not add --mcp flags manually.
4. Dashboard shows bridge connection, health, phase, commit, tick, last reply and process exit. Missing/unsupported data appears unavailable, not as a healthy server.

Native listeners bind only 127.0.0.1. Docker creates an instance-owned internal bridge network containing only Supervisor and game server; no bridge host port is published. Supervisor binds its private network address. Changing Docker bridge mode recreates the stopped owned game container, preserves storage and keeps its previous stopped container and private inspect JSON under the state directory for manual recovery. Only the Supervisor has Docker access.

The token stays in the Supervisor and native/container launch arguments; it is never returned to the browser. Authentication also validates process realm, native ownership or Docker namespace PID and private peer address. Local administrators with process-inspection rights can still inspect their own launch arguments.

## Console and logs

Console uses correlated command replies while the bridge is available. Requests with a missing reply are unconfirmed and are never resent through PTY. Inspect state/logs before manually retrying. Bundle commands still require a new matching lifecycle event and a final state check.

Stream entries retain source sequence, source and module. Received-at time is a panel observation time, not an invented game timestamp. New connections get new stream identities; panel cursors remain monotonic. Gaps are shown explicitly. Health follows upstream tick/response checks; it is not a player-count, FPS or inventory feed.

Source/module filters, search, pause/resume and downloads respect the existing console/log permissions. Pause affects display only. Download exports currently displayed filtered lines (up to 2000). Stored history is read in pages; the visible window remains bounded.

History stays locally in the private state directory/diagnostics, defaults to 7 days and 100 MB total, and rotates in approximately 4 MB files. Limits are configurable (1–30 days, 10–1000 MB). Retention deletes expired/oldest archive segments. Keep state private and back it up if needed. No telemetry is sent externally.

PTY process controls, console and file logs remain available when the bridge is unavailable. A future official public bridge flag will require a tested launch-policy update; this release does not bypass sv_lan. Local testclient support is reserved for a later permission-controlled developer feature.

## API and development

Authenticated GET /api/bridge returns bridge diagnosis and non-secret settings. POST /api/bridge/settings requires settings.write and CSRF, and accepts enabled, port, retentionDays and maxMegabytes. GET /api/diagnostics accepts cursor, source, module, search, limit (1–500), history; returns cursor, dropped and structured lines. Console source requires console.read; log/panel requires logs.read; all requires both. Existing HTTP routes remain compatible.

Run `dotnet run --project tests/Bridge.Tests`. For isolated native acceptance use `PANEL_TEST_BUILD` and `PANEL_BRIDGE_TEST_SERVER` with `node scripts/test-bridge.mjs`; the test server must be under .local and use separate game/voice ports. Installer CI checks actual Windows/Linux/Docker bridge behavior. Upstream reference is pinned in THIRD_PARTY_NOTICES.md and the complete MIT notice is shipped.

## Deutsch

Standardmäßig bleibt PTY aktiv. Unter Einstellungen **LAN-Bridge** aufklappen, einen freien internen Port wählen (20774), speichern und ausdrücklich neu starten. Aktuelle offizielle Serverdateien sind erforderlich. **--mcp erzwingt sv_lan 1; der Server erscheint nicht in der öffentlichen Liste.** cfg/server.cfg wird nicht verändert. Öffentliche Server behalten PTY.

Windows/Linux verwenden nur localhost. Docker nutzt ein privates instanzeigenes Netz ohne veröffentlichten Bridge-Port. Beim bestätigten Wechsel wird der gestoppte verwaltete Spielcontainer neu erstellt; Speicher bleibt, alter Container und private Definition bleiben für Wiederherstellung erhalten.

Dashboard zeigt Verbindung, Health, Phase, Commit, Tick, letzte Antwort und Prozessende. Empfangszeit ist eine Beobachtung des Panels. Sequenznummern, Module, Quellen und erkennbare Lücken bleiben erhalten. Filter, Pause, Historie und Download beachten bestehende Rechte. Download enthält bis zu 2000 angezeigte gefilterte Zeilen. Historie bleibt lokal: standardmäßig sieben Tage / insgesamt 100 MB, konfigurierbar. Es erfolgt kein externer Versand.

Bei Verbindungsproblemen bleiben PTY und Prozessverwaltung verfügbar. Schreibbefehle werden nach Timeout nicht erneut gesendet; Zustand vor erneutem Versuch prüfen. Für öffentlichen Bridge-Betrieb muss zuerst ein offizieller Parameter unterstützt und getestet werden. Testclients sind eine spätere Erweiterung.

## Docker recovery / Wiederherstellung

Stop Supervisor and Webhost first. Inspect the private docker-before-bridge-*.json backup and verify its instance label. The old stopped container is retained under a -previous- name. Stop the replacement game container, restore the saved old container ID in private panel.json and its original name if needed, disable Bridge.Enabled and restart the stack. Preserve the server mount and all state. Do not start old and new containers together: both bind the same game ports. Recovery is local administrator work; no arbitrary container selector is exposed in the dashboard.
