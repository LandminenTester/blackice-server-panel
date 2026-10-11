# Build from source / Aus dem Quellcode bauen

## Deutsch

### Terminal öffnen und im richtigen Ordner arbeiten

Die folgenden Befehle im integrierten VS-Code-Terminal oder einem selbst geöffneten PowerShell-/Linux-Terminal ausführen. Ein Buildfehler beendet dann nicht das Fenster. Ein roter Fehler ist kein erfolgreicher Build: erst die erste Fehlermeldung beheben, danach denselben Schritt erneut ausführen.

Das Terminal muss im Repositoryordner stehen, der `global.json`, `ui` und `src` enthält. Nicht im BlackICE-Spielserverordner und nicht in `artifacts/web` bauen.

```powershell
cd "C:\Pfad\zum\blackice-server-panel"
git --version
dotnet --version
node --version
npm --version
```

Voraussetzungen: Git, .NET SDK **10.0.401**, Node **24.19.0** und npm. Die SDK-Auswahl steht in `global.json`, npm-Versionen stehen in `ui/package-lock.json`. Releases enthalten die .NET-Laufzeit; Node und SDK werden nur zum Entwickeln benötigt.

### Laufende Entwicklung stoppen

Vor dem Ersetzen von `artifacts` im sichtbaren Startterminal `Q` wählen. Das stoppt den Spielserver, Webhost, Supervisor und Serverterminal geordnet. Ein laufender Vite-Devserver wird in seinem eigenen Terminal mit Strg+C beendet. Laufende Programme können unter Windows DLLs oder das native Rolldown-Buildmodul sperren.

Private Konfigurationen bleiben unter `.local`. Beim Bauen diese Dateien und den echten Serverordner nicht löschen.

### Windows: Schritt für Schritt

```powershell
npm --prefix ui ci
npm --prefix ui run check
npm --prefix ui run build

dotnet build src/Supervisor -c Release
dotnet build src/Web -c Release

dotnet publish src/Supervisor -c Release -r win-x64 --self-contained true -o artifacts/supervisor
dotnet publish src/Web -c Release -r win-x64 --self-contained true -o artifacts/web

dotnet publish src/Supervisor -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .local/launcher
Copy-Item .local/launcher/Supervisor.exe Start.exe -Force
 # Keep the repository VERSION file beside Start.exe; match Directory.Build.props when changing the version.
Copy-Item licenses artifacts/web/wwwroot/licenses -Recurse -Force
.\Start.exe
```

Die UI-Buildausgabe liegt unter `src/Web/wwwroot`. `dotnet build` prüft und kompiliert C#, erstellt aber noch kein vollständiges Betriebspaket. `dotnet publish` erzeugt die Programme samt Laufzeit in `artifacts`. Der letzte Publish erzeugt die einzelne Startdatei; sie bleibt neben dem Ordner `artifacts`.

Jeden Schritt einzeln ausführen und auf seinen Erfolg warten. Nicht nach einem fehlgeschlagenen npm-Build mit einem alten UI-Artefakt weiter veröffentlichen.

### Linux: Schritt für Schritt

Referenz: Debian 13, x86-64. Erst Git, .NET SDK und Node installieren. Zusätzlich:

```sh
sudo apt install build-essential ca-certificates libicu76 libssl3t64 libstdc++6 zlib1g tmux
# Nur für eine grafische Desktopumgebung:
sudo apt install xterm
cd /pfad/zum/blackice-server-panel

npm --prefix ui ci
npm --prefix ui run check
npm --prefix ui run build
dotnet build src/Supervisor -c Release
dotnet build src/Web -c Release

dotnet publish src/Supervisor -c Release -r linux-x64 --self-contained true -o artifacts/supervisor
dotnet publish src/Web -c Release -r linux-x64 --self-contained true -o artifacts/web
cc -O2 -Wall -Wextra native/terminal.c -lutil -o artifacts/supervisor/replay-terminal

dotnet publish src/Supervisor -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .local/launcher
cp .local/launcher/Supervisor Start
chmod +x Start
cp -R licenses artifacts/web/wwwroot/
./Start
```

Der kleine C-Helfer erzeugt eine echte PTY für BlackICE. Für den Betrieb ist er im Release enthalten; zum lokalen Bau werden cc und libutil benötigt. Auf einem Desktop erscheint ein zusätzliches Serverterminal. Per SSH wird eine getrennte tmux-Sitzung verwendet; `C` im Startterminal hängt diese an. Mit Strg+B, danach D, zurück zum Steuerterminal wechseln. `exit` in der Serverkonsole stoppt den Server; `Q` im Steuerterminal beendet alles.

### Docker bauen

```sh
docker version
docker build -t replay-panel:local .
```

Docker muss laufen. Der mehrstufige Build baut UI, C#-Programme und PTY-Helfer im Image; auf dem Host werden dafür weder Node noch SDK benötigt. Für Betrieb und Volumes siehe [Docker-Einrichtung](docker.md). Webhost und Supervisor bleiben getrennte Container. Docker öffnet kein zusätzliches Serverfenster.

### Tests

```powershell
dotnet run --project tests/Core.Tests
dotnet publish tests/ConsoleFixture -c Release -o .local/fixture
dotnet run --project tests/Backend.Tests -- .local/fixture
node scripts/test-panel.mjs
node scripts/test-setup.mjs
```

Die Fixturetests verwenden getrennte Ordner und Ports. Der Launcher-Test verlangt ein isoliertes Paket unter `.local`; niemals die echte Konfiguration damit überschreiben. Die CI baut Windows und Linux, prüft Launcher/Serverterminal und testet den echten offiziellen Download unter Linux/Docker.

### Häufige Fehler

| Meldung | Ursache und Vorgehen |
|---|---|
| `MSB1003` / kein Projekt gefunden | Projekt ausdrücklich angeben: `dotnet build src/Web`, im Repositoryordner arbeiten. |
| SDK nicht gefunden / `NETSDK1045` | Mit `dotnet --list-sdks` prüfen und SDK 10.0.401 installieren. Eine Laufzeit allein genügt zum Bauen nicht. |
| `svelte-check` oder `vite` fehlt | Im Repository `npm --prefix ui ci` ausführen. |
| `EPERM unlink ... rolldown ... .node` | Native Builddatei ist gesperrt. Den betreffenden Devserver/Editor-Language-Server stoppen. Bei Bedarf `ui/node_modules` innerhalb des Projekts als Sicherung verschieben und `npm ci` erneut ausführen. Keine fremden Node-Prozesse pauschal beenden. |
| `MSB3027` / Datei wird verwendet | Laufenden Stack mit `Q` stoppen oder in einen anderen Ausgabeordner veröffentlichen. |
| `NU1301` / npm-Netzwerkfehler | Verbindung, Proxy und Zertifikate prüfen; den Restore wiederholen. TLS-Prüfung nicht abschalten. |
| `cc` oder `libutil` fehlt | `build-essential` installieren und den obigen Helper-Befehl ausführen. |
| Linux: Permission denied | Programm ausführbar machen; Paket- und Zustandsordner müssen dem Dienstbenutzer zugänglich beziehungsweise beschreibbar sein. |
| Port 20772 belegt | Bereits laufendes Panel verwenden oder dessen Startterminal geordnet beenden; nicht einfach einen zweiten Stack starten. |
| Start meldet fehlende Dateien | `Start` muss neben `artifacts` liegen. Beide Programmpakete veröffentlichen. |

Buildausgabe für eine Fehleranalyse behalten:

```powershell
dotnet build src/Web -c Release 2>&1 | Tee-Object -FilePath build-web.log
```
```sh
dotnet build src/Web -c Release 2>&1 | tee build-web.log
```

## English

Open VS Code's integrated terminal or a terminal yourself and keep it open. Run from the repository containing `global.json`, `src` and `ui`. Do not double-click build scripts and do not build in the game server folder. A red error means the step failed; fix the first error before continuing.

Install Git, .NET SDK **10.0.401**, Node **24.19.0** and npm. Linux additionally needs `build-essential` and the runtime libraries shown above. Releases contain .NET and need neither Node nor SDK.

Use the exact Windows or Linux command block above, one command at a time:
1. `npm ci` restores locked dependencies.
2. `npm run check` checks Svelte/TypeScript.
3. `npm run build` produces `src/Web/wwwroot`.
4. `dotnet build` validates C#.
5. `dotnet publish` produces self-contained Supervisor/Web packages.
6. The single-file publish creates the direct `Start.exe`/`Start` entry; keep it beside `artifacts`.
7. Linux requires the bundled PTY helper; compile it using the shown cc command.

Before replacing active binaries choose `Q` in the Start terminal. Stop Vite separately with Ctrl+C. Keep private `.local` configuration and all real game data. To build while an old release runs, publish into a separate staging directory instead of overwriting loaded DLLs.

Windows opens a control terminal and a separate BlackICE server terminal. Linux desktop uses a terminal emulator; SSH uses tmux. `C` attaches the server session, Ctrl+B then D returns to control, `exit` stops the game, and `Q` shuts down everything. Docker uses the shown image build and separate services without native windows.

Troubleshooting:
- MSB1003: specify `src/Web` or `src/Supervisor` and work from the repository.
- NETSDK1045: install the SDK specified by global.json, not just a runtime.
- Missing vite/svelte-check: run `npm --prefix ui ci`.
- EPERM on a Rolldown native module: stop the relevant dev/language-server process; optionally move dependencies into a project-local backup and restore. Never kill all Node processes.
- MSB3027: stop this panel before replacing DLLs.
- NU1301/network: check proxy, certificates and connectivity; retry without disabling TLS checks.
- Missing cc/libutil: install build-essential and compile the helper.
- Permission denied: restore executable/writable permissions for the service account.
- Port conflict: use or gracefully stop the existing panel.
- Missing release files: keep Start beside both artifact directories.

Use `Tee-Object` on Windows or `tee` on Linux, as shown above, to retain the complete build output. [Architecture and tests](development.md) · [Operation](operations.md).
