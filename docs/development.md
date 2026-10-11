# Development / Entwicklung

[Complete step-by-step build guide / Vollständige Build-Anleitung](building.md)

## English

Prerequisites: Git, .NET SDK 10.0.401 (global.json), Node 24.19.0 and npm. Linux also needs cc, libc headers and libutil. Editor suggestions: VS Code with C# and Svelte support.

Follow the individual Windows/Linux commands in [Building](building.md) from an already open terminal. The build scripts are optional conveniences. Restore exact npm versions with `npm --prefix ui ci`; .NET lockfiles are committed. Run `npm --prefix ui run check` before building.

Backend contract: `IServerBackend` exposes status, start, stop/force, command, console and logs. Platform integrations live in Supervisor; Web never accesses game files or Docker Engine. BundleManager serializes console queries and actions, delimits responses using echo markers, and waits for exact native lifecycle events. Loaded bundles can be stopped: never infer running state from the loaded list alone.

Private IPC uses JSON-line request/reply, current-user pipes on Windows and 0600 Unix sockets on Linux. Setup routes forward opaque payloads to Supervisor, which owns the setup key, password hashes, filesystem checks and installation jobs.

Typed `ui/src/locales.ts` defines English keys and requires a complete German catalog. Every user-facing error uses a stable code. Add a code to both catalogs when expanding the backend. BlackICE logs, usernames and bundle names remain original. UI controls live under `ui/src/components`; preserve keyboard, focus and reduced-motion behavior.

Tests:
- `dotnet publish tests/ConsoleFixture -c Release -o .local/fixture`
- `dotnet run --project tests/Backend.Tests -- .local/fixture` (on Linux place replay-terminal beside the test executable).
- `node scripts/test-panel.mjs`: disposable console fixture only, port 20774.
- `node scripts/test-setup.mjs`: protected wizard and concurrency, port 20775.
- `node scripts/test-install.mjs`: actual Windows download, isolated server ports 30770/30771.
- `docker build -f tests/Linux.Dockerfile -t replay-panel-linux-tests .`; `docker run --rm replay-panel-linux-tests`.
- `docker build -t replay-panel:next .`; `node scripts/test-install.mjs --linux` / `--docker`: Debian 13 and Engine integration, disposable volumes and HTTP port 20776.

Never point fixture tests at a production directory. Runtime downloads are vendor artifacts and subject to upstream availability. Release builds package Windows/Linux self-contained runtimes, Linux terminal helper, docs and scripts; Docker uses separate services and a versioned image.

## Deutsch

Voraussetzungen: Git, .NET SDK 10.0.401 aus global.json, Node 24.19.0 und npm. Linux benötigt zusätzlich cc, libc-Header und libutil. VS Code mit C#- und Svelte-Erweiterung eignet sich als Editor.

Die einzelnen Windows-/Linux-Befehle aus [Builden](building.md) im offenen Terminal ausführen. Buildskripte sind optionale Hilfen. Paketversionen mit `npm --prefix ui ci` wiederherstellen; .NET-Lockdateien sind versioniert. Vor dem Build `npm --prefix ui run check` ausführen.

`IServerBackend` definiert Status, Start, Stop/Force, Befehl, Konsole und Logs. Plattformintegration gehört in den Supervisor. Der Webhost greift weder auf Spieldateien noch auf Docker Engine zu. BundleManager serialisiert Abfragen und Aktionen, begrenzt Antworten durch Echo-Marker und wartet auf echte Lebenszyklusereignisse. Geladene Bundles können gestoppt sein.

Die interne Verbindung nutzt JSON-Zeilen, Benutzer-Pipes unter Windows und Unix-Sockets mit 0600 unter Linux. Der Supervisor besitzt Einrichtungsschlüssel, Passwort-Hashes, Dateiprüfung und Installationsaufträge.

`ui/src/locales.ts` definiert englische Schlüssel und erzwingt die vollständige deutsche Übersetzung. Jeder neue Fehlercode muss in beiden Katalogen stehen. BlackICE-Logs, Benutzer- und Bundlenamen werden nicht übersetzt. Komponenten in `ui/src/components` müssen Tastaturbedienung, Fokus und reduzierte Bewegung erhalten.

Die oben aufgeführten Tests verwenden eigene Ports, Prozesse und Ordner. Niemals Produktionsverzeichnisse an Fixturetests übergeben. Offizielle Downloads hängen von der Anbietererreichbarkeit ab. Releases enthalten Windows-/Linux-Laufzeit, PTY-Helfer, Dokumentation und Skripte; Docker verwendet getrennte Dienste und ein versioniertes Image.
