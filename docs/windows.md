# Windows setup / Einrichtung

## English

Windows 10 1809+ x86-64 with ConPTY. BlackICE requires a roughly 2013+ x86-64 CPU, 512 MB RAM minimum for the server alone (2 GB+ recommended), and several GB disk space.

1. Extract the release outside the BlackICE server directory.
2. Open `Start.exe`. The control terminal stays open.
3. Open `http://127.0.0.1:20772`. Read the setup key from `.local/supervisor.log`.
4. Enter dashboard name, username and matching passwords (at least 8 characters).
5. The server folder `server` inside the panel folder is created automatically; use "Change folder" to pick another one with the system folder dialog. An existing server needs `REServer.exe` and `cfg/server.cfg`; format and write access are checked.
6. For installation, select an empty directory. The official ZIP is staged, extracted with path checks and validated. Cancel and retry are available; existing files are preserved.
7. Finish and sign in. The server starts automatically by default; change this under Settings.

Both processes run as the same account. Start protects `.local` for that user and SYSTEM. Never publish private state. While setup is incomplete, a Supervisor restart generates a new setup key.

`scripts/stop-web.ps1` stops only the Webhost. `scripts/control.ps1 -Action status|start|stop|restart|shutdown` controls the Supervisor. Stop the game before shutting down the Supervisor. For autostart use Task Scheduler under the same account, running Start.exe at sign-in.

Windows native address flags can fail before Winsock initialization. Set `sv_bindaddr "127.0.0.1:20770"` and `sv_voip_addr "127.0.0.1:20771"` in `cfg/server.cfg` instead.

## Deutsch

Windows 10 ab 1809, x86-64 und ConPTY verwenden. BlackICE benötigt eine CPU ab etwa 2013, mindestens 512 MB RAM nur für den Server (2 GB empfohlen) und mehrere GB Speicher.

1. Paket getrennt vom Server entpacken.
2. `Start.exe` öffnen. Das Steuerterminal bleibt offen.
3. `http://127.0.0.1:20772` öffnen, Schlüssel in `.local/supervisor.log` lesen.
4. Dashboardname, Benutzername und identische Passwörter mit mindestens 8 Zeichen festlegen.
5. Der Ordner `server` im Panel-Ordner wird automatisch angelegt; „Ordner ändern“ öffnet den System-Ordnerdialog. Vorhandene Server benötigen `REServer.exe` und `cfg/server.cfg`. Dateiformat und Schreibrechte werden geprüft.
6. Für den offiziellen Download einen leeren Ordner wählen. Der Installer arbeitet in einem temporären Verzeichnis mit Pfad- und Dateiprüfung. Abbruch und Wiederholung sind möglich; vorhandene Dateien bleiben erhalten.
7. Einrichtung abschließen und anmelden. Standardmäßig startet der Server automatisch; dies lässt sich unter Einstellungen ändern.

Beide Prozesse laufen unter demselben Konto. Start schützt `.local` für den Benutzer und SYSTEM. Private Zustandsdateien nicht veröffentlichen. Vor abgeschlossener Einrichtung erzeugt ein Supervisor-Neustart einen neuen Schlüssel.

`scripts/stop-web.ps1` beendet nur den Webhost. `scripts/control.ps1 -Action status|start|stop|restart|shutdown` bedient den Supervisor. Vor dessen Beenden den Spielserver stoppen. Für Autostart eine Aufgabe bei Benutzeranmeldung unter demselben Konto verwenden.

Windows-Adressargumente können vor der Winsock-Initialisierung scheitern. Stattdessen `sv_bindaddr "127.0.0.1:20770"` und `sv_voip_addr "127.0.0.1:20771"` in `cfg/server.cfg` eintragen.

## Direct terminal lifecycle / Direkter Terminalbetrieb

EN: Start opens the control terminal and, for a managed native game, a separate server terminal. Q or Ctrl+C in Start stops the complete stack. Closing its X delegates graceful shutdown to an independent worker; inspect .local/controller-stop.log if it fails. Closing the server terminal or entering exit stops only the game. Settings → Start server with the panel controls auto-start. K explicitly keeps services running when leaving control; W stops only Webhost according to its configured shutdown mode. For unattended use, use service/background operation rather than an interactive Start window.

DE: Start öffnet das Steuerterminal und für den verwalteten nativen Server ein eigenes Serverterminal. Q oder Strg+C im Startfenster beendet den gesamten Stack. Das X delegiert geordnetes Beenden an einen unabhängigen Prozess; bei Fehlern .local/controller-stop.log prüfen. Das Serverterminal per X oder mit exit zu schließen stoppt nur den Spielserver. Unter Einstellungen → Server mit dem Panel starten wird Autostart festgelegt. K lässt Dienste ausdrücklich weiterlaufen, W beendet nur den Webhost nach dessen Abschaltmodus. Für unbeaufsichtigten Betrieb Dienst-/Hintergrundstart verwenden.

[Build guide / Build-Anleitung](building.md)
