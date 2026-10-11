# Windows setup / Einrichtung

## English

Windows 10 1809+ x86-64 with ConPTY. Replay requires a roughly 2013+ x86-64 CPU, 512 MB RAM minimum for the server alone (2 GB+ recommended), and several GB disk space.

1. Extract the release outside the Replay server directory.
2. Run `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\start.ps1`.
3. Open `http://127.0.0.1:20772`. Read the setup key from `.local/supervisor.log`.
4. Enter dashboard name, username and matching passwords (at least 12 characters).
5. Browse directories on the host or enter an absolute path. An existing server needs `REServer.exe` and `cfg/server.cfg`; format and write access are checked.
6. For installation, select an empty directory. The official ZIP is staged, extracted with path checks and validated. Cancel and retry are available; existing files are preserved.
7. Finish, sign in and start the server.

Both processes run as the same account. The launch script protects `.local` for that user and SYSTEM. Never publish private state. While setup is incomplete, a Supervisor restart generates a new setup key.

`scripts/stop-web.ps1` stops only the Webhost. `scripts/control.ps1 -Action status|start|stop|restart|shutdown` controls the Supervisor. Stop the game before shutting down the Supervisor. For autostart use Task Scheduler under the same account, running the start script at sign-in.

Windows native address flags can fail before Winsock initialization. Set `sv_bindaddr "127.0.0.1:20770"` and `sv_voip_addr "127.0.0.1:20771"` in `cfg/server.cfg` instead.

## Deutsch

Windows 10 ab 1809, x86-64 und ConPTY verwenden. Replay benötigt eine CPU ab etwa 2013, mindestens 512 MB RAM nur für den Server (2 GB empfohlen) und mehrere GB Speicher.

1. Paket getrennt vom Server entpacken.
2. `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\start.ps1` ausführen.
3. `http://127.0.0.1:20772` öffnen, Schlüssel in `.local/supervisor.log` lesen.
4. Dashboardname, Benutzername und identische Passwörter mit mindestens 12 Zeichen festlegen.
5. Hostordner durchsuchen oder absoluten Pfad eingeben. Vorhandene Server benötigen `REServer.exe` und `cfg/server.cfg`. Dateiformat und Schreibrechte werden geprüft.
6. Für den offiziellen Download einen leeren Ordner wählen. Der Installer arbeitet in einem temporären Verzeichnis mit Pfad- und Dateiprüfung. Abbruch und Wiederholung sind möglich; vorhandene Dateien bleiben erhalten.
7. Einrichtung abschließen, anmelden und starten.

Beide Prozesse laufen unter demselben Konto. Das Startskript schützt `.local` für den Benutzer und SYSTEM. Private Zustandsdateien nicht veröffentlichen. Vor abgeschlossener Einrichtung erzeugt ein Supervisor-Neustart einen neuen Schlüssel.

`scripts/stop-web.ps1` beendet nur den Webhost. `scripts/control.ps1 -Action status|start|stop|restart|shutdown` bedient den Supervisor. Vor dessen Beenden den Spielserver stoppen. Für Autostart eine Aufgabe bei Benutzeranmeldung unter demselben Konto verwenden.

Windows-Adressargumente können vor der Winsock-Initialisierung scheitern. Stattdessen `sv_bindaddr "127.0.0.1:20770"` und `sv_voip_addr "127.0.0.1:20771"` in `cfg/server.cfg` eintragen.
