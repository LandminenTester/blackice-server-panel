# Replay Server Panel

Eigenständiges lokales Windows-Webpanel für den ReplayRE / BlackICE Dedicated Server. MIT-Lizenz. Es benötigt weder ein RP-Framework noch eine Datenbank.

## Einstieg unter Windows

Quellcode-Entwicklung benötigt .NET SDK 10.0.401, Node.js 24 und Git. VS Code mit C#- und Svelte-Erweiterung ist empfohlen.

In PowerShell im Projektordner:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/setup.ps1 -ServerRoot "C:/Replay/Server"
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/start.ps1
```

Öffne http://127.0.0.1:20772. Das generierte Passwort liegt ausschließlich in **.local/login.txt**. Diese Datei und alle Laufzeitdaten sind von Git ausgeschlossen. Setup überschreibt vorhandene Einstellungen nicht.

Der Serverordner muss REServer.exe, die Replay-DLLs und dessen Konfiguration enthalten. Der Server wird erst über **Server starten** gestartet. Ein bereits anderweitig gestarteter Server wird angezeigt, aber nicht übernommen oder beendet. Beende ihn in seiner bisherigen Konsole mit `exit` und starte ihn anschließend über das Panel.

## Bedienung

- Start, geordneter Stop und Neustart mit expliziter Prozesszuordnung.
- Live-Konsole und aktuelle Logdatei.
- Replay-Konsolenbefehle, etwa `bundles`, `start replay-rp` oder `restart replay-rp`.
- Erzwungener Stop als getrennte bestätigte Aktion.
- Modus **Server weiterlaufen lassen** (Standard) oder **Server geordnet stoppen** beim Beenden des Webhosts.
- Das Schließen des Browser-Tabs ändert den Serverzustand nicht.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/stop-web.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/control.ps1 -Action status
```

Der Supervisor läuft im Modus `keep-running` weiter. Der Webhost lässt sich mit `start.ps1` erneut verbinden. Um alles geordnet zu beenden, zuerst `control.ps1 -Action stop`, dann `stop-web.ps1` und zuletzt `control.ps1 -Action shutdown`. `shutdown` wird abgelehnt, solange der verwaltete Spielserver noch läuft.

## Architektur

Browser → ASP.NET Core Webhost → lokale Named Pipe → Supervisor → ConPTY → REServer.exe.

Die Named Pipes sind auf den aktuellen Windows-Benutzer beschränkt. Supervisor und Webhost sind separate Prozesse. Der Supervisor hält die Konsole auch ohne Weboberfläche offen. Eine Prüfung des Executable-Pfads anhand des gehaltenen Prozesshandles schützt vor Verwechslungen mit wiederverwendeten PIDs.

ConPTY wird ohne geerbte Standard-Loghandles erzeugt. Das ist für den Betrieb aus einer Anwendung mit umgeleiteter Ausgabe wichtig. Konsolentext wird als Text angezeigt; Terminal-Steuersequenzen werden entfernt.

Konfiguration: `.local/panel.json`, Vorlage `panel.example.json`. `serverArguments` ist eine optionale Liste nativer Serverargumente; es wird keine Shell gestartet. Native Windows-Adressflags können schon vor Winsock-Initialisierung scheitern. Adressen daher bevorzugt in `cfg/server.cfg` setzen: `sv_bindaddr` und `sv_voip_addr`.

`autoRestart` ist standardmäßig `false`. Bei `true` kann der Supervisor unerwartete Prozessabbrüche mit einer Pause neu starten. Ein bewusst ausgelöster Stop schaltet den gewünschten Laufzustand aus.

## Sicherheit und Grenzen

Nur Loopback-HTTP ist in dieser ersten Version erlaubt. Passwortprüfung mit PBKDF2, signierte HttpOnly-Sitzung, SameSite=Strict, CSRF-Schutz, Host-Prüfung und Anmelderatenbegrenzung. Keine allgemeine Windows-Shell. `exit` und `update` werden nicht als rohe Konsolenaktion ausgeführt; Stop ist ein eigener Ablauf und Updates bleiben ein geplantes Wartungsfenster.

Der Supervisor ist erforderlich, solange er eine ConPTY-Sitzung hält. Webhost-Abschaltung lässt ihn weiterlaufen; das Beenden des Supervisors ist ein anderer Vorgang. Bei einem Supervisor-Absturz ist kein unterbrechungsfreier Fortbestand der Konsole garantiert.

Vor einem Quellcode-Neubau Webhost und Supervisor beenden, damit Windows deren Binärdateien freigibt. Das Beenden des Supervisors setzt einen geordnet gestoppten Spielserver voraus. Frontend-only Änderungen können separat gebaut und ihre statischen Dateien aktualisiert werden.

## Prüfen

```powershell
dotnet publish tests/ConsoleFixture -c Release -o .local/fixture
dotnet run --project tests/Backend.Tests -- .local/fixture
node scripts/test-panel.mjs
```

Der letzte Test benötigt bereits gebaute `artifacts/web` und `artifacts/supervisor`. Er verwendet ausschließlich die wegwerfbare ConsoleFixture auf Panelport 20774 und prüft Anmeldung, CSRF, Prozesssteuerung, Befehle und beide Abschaltmodi. Der feste Testzugang ist keine echte Panel-Zugangsinformation. Die Fixture ist kein Replay-Spielserver.

Ein separater echter Replay-Test kann mit einem isolierten Serverordner und anderen Spiel-/Voiceports durchgeführt werden. Er darf nicht auf einer bestehenden Spielinstanz laufen.

## Releases und späterer Linux-Betrieb

GitHub Actions baut und testet unter Windows. Tags `v*` erzeugen ein Releasepaket mit selbstenthaltenden win-x64-Binaries, sodass die .NET-Laufzeit für deren Betrieb nicht separat installiert werden muss. Setup verwendet derzeit Node.js zum Erzeugen der lokalen Konfiguration.

`IServerBackend` hält die Prozessverwaltung austauschbar. Ein Linux-/Docker-Backend und eine öffentliche Bereitstellung sind noch nicht implementiert. Bei diesem späteren Ausbau: Panel hinter HTTPS, interne Steuerverbindung, begrenzter Containerzugriff und keine direkte Freigabe des Docker-Sockets an Browser oder öffentlichen Webhost.

## Quellen

- [Replay Serververwaltung](https://docs.replay.re/servers/managing/)
- [Windows ConPTY](https://learn.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session)
