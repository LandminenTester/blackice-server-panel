# BlackICE Server Panel

Inoffizielles Dashboard für eine **BlackICE-/REServer-Instanz**: Serversteuerung, Live-Konsole, Logs, Bundle-Verwaltung, mehrere Benutzer mit einzelnen Berechtigungen, Deutsch/Englisch, geschützte Ersteinrichtung, offizieller Serverdownload und Cyberpunk-inspiriertes Design.

[English](README.md) · [Release 1.0](https://github.com/LandminenTester/blackice-server-panel/releases/tag/v1.0.0)

## Windows

1. `blackice-server-panel-win-x64.zip` herunterladen und in einen beschreibbaren Ordner **getrennt vom Spielserver** entpacken.
2. `Start.exe` öffnen und das Steuerterminal offen lassen.
3. http://127.0.0.1:20772 öffnen. Einrichtungsschlüssel aus dem Terminal oder `.local/supervisor.log` verwenden, Administrator anlegen und vorhandenen Server oder einen leeren Downloadordner auswählen.

Windows x86-64 erforderlich; .NET ist enthalten. SDK und Node.js sind nicht nötig. Der Spielserver öffnet eine eigene Konsole. Diese schließen stoppt das Spiel; Start schließen, Strg+C oder Q beendet den gesamten Stack. Autostart lässt sich unter Einstellungen aktivieren.

## Natives Linux

Referenzplattform: Debian 13 x86-64 mit glibc. ARM und Alpine/musl werden nicht unterstützt. `blackice-server-panel-linux-x64.tar.gz` herunterladen, dann:

```sh
sudo apt update
sudo apt install ca-certificates libicu76 libssl3t64 libstdc++6 zlib1g tmux
mkdir -p ~/blackice-panel
tar -xzf blackice-server-panel-linux-x64.tar.gz -C ~/blackice-panel
cd ~/blackice-panel
chmod +x Start
./Start
```

Einrichtung unter localhost:20772 wie oben abschließen. Laufzeit und PTY-Helfer sind enthalten; der Serverdownload benötigt keinen Docker-Daemon. Per SSH verwendet die Serverkonsole tmux: C verbindet, Strg+B und danach D trennt die Ansicht. Auf dem Desktop `xterm` für eigene Terminalfenster installieren. [Dienste und Bibliotheksprüfung](docs/linux.md).

## Docker unter Linux

Voraussetzungen: Linux x86-64, Docker Engine und Compose v2. Ein natives Releasepaket für `deploy/` entpacken und `blackice-server-panel-docker.tar.gz` aus demselben Release laden.

```sh
gzip -dc blackice-server-panel-docker.tar.gz | docker load
cp deploy/compose.env.example .env
sudo mkdir -p /srv/blackice-panel/state /srv/blackice-panel/runtime /srv/blackice-server
sudo chown 10001:10001 /srv/blackice-panel/state /srv/blackice-panel/runtime /srv/blackice-server
sudo chmod 700 /srv/blackice-panel/state /srv/blackice-panel/runtime /srv/blackice-server
stat -c %g /var/run/docker.sock
```

In `.env` eintragen: `PANEL_STATE=/srv/blackice-panel/state`, `PANEL_RUNTIME=/srv/blackice-panel/runtime`, `SERVER_PATH=/srv/blackice-server`, `DOCKER_GID` mit der ausgegebenen Socket-Gruppen-ID und `PANEL_VERSION=1.0.0`. Dann:

```sh
docker compose --env-file .env -f deploy/compose.yaml up -d
docker compose --env-file .env -f deploy/compose.yaml logs supervisor
```

Unter localhost:20772 den ausgegebenen Einrichtungsschlüssel verwenden, Docker und `/server` wählen. Nur der Supervisor erhält den weitreichenden Docker-Socket, der Webhost nicht. Panelzustand und Spieldateien bleiben in Hostordnern. Beenden: `docker compose --env-file .env -f deploy/compose.yaml down`. [Docker-Details](docs/docker.md).

## Bedienung und Updates

- Header: Status, Starten, Neustarten und Stoppen. Geordneter Stop wartet bis zu 30 Sekunden; erzwungener Stop ist separat. Bundle-Aktionen warten auf Serverbestätigung.
- Benutzer: Rechte für Server, Konsole, Logs, Bundles, Einstellungen und Benutzerverwaltung einzeln vergeben. Konsolenschreibzugriff nur vertrauenswürdigen Administratoren geben.
- Einstellungen → Panel-Updates: stabile GitHub-Releases prüfen, mit SHA-256 verifizieren und mit Sicherung sowie Wiederherstellung bei Startfehlern installieren. Konfiguration, Benutzer und Spieldateien bleiben erhalten. Installation über native Startdateien; Docker/systemd werden über ihre Bereitstellungswerkzeuge aktualisiert. **0.5.1 → 1.0 einmal manuell ersetzen, da das Repository umbenannt wurde; `.local/` erhalten.** [Updates und Wiederherstellung](docs/updates.md).
- Browser-Tab schließen stoppt nichts. `keep-running` / `stop-server` steuert das Beenden des Webhosts; Start schließen beendet immer den gesamten Stack. Externe Server werden erkannt und nicht doppelt gestartet; ihre Konsole kann nicht übernommen werden.
- Ports: Spiel/Bundle-Download 20770 TCP/UDP, Voice 20771 TCP/UDP, Dashboard 20772 TCP auf localhost. Fernzugriff über dokumentierten [HTTPS-Reverse-Proxy](docs/operations.md).
- `.local/` und Spielserverordner sichern. Neue Downloads verwenden LAN-Modus und deaktivierte automatische Serverupdates. Öffentliche Server benötigen die [offizielle Registrierung](https://docs.replay.re/servers/registering/).

## Entwicklung

.NET SDK 10 und Node.js 24; Linux zusätzlich C-Compiler und tmux. [Buildbefehle und Fehlerdiagnose](docs/building.md) · [Architektur](docs/development.md) · [Berechtigungen](docs/users.md). Windows/Linux-CI prüft Backends, Anmeldung, Einrichtung, Bundles, Launcher und Update-Rollback. Docker-CI prüft echte Serverinstallationen.

## Links und Hinweise

[BlackICE von REPLAY.re](https://replay.re/) · [Offizielle Dokumentation](https://docs.replay.re/) · [Offizielles Account-/Serverpanel](https://panel.replay.re/) · [Forum](https://forum.replay.re/)

Community-Projekt unter MIT-Lizenz; keine Verbindung zu oder offizielle Unterstützung durch REPLAY.re, BlackICE oder CD PROJEKT RED. Eigenes Cyberpunk-inspiriertes Design; [visuelle Referenz](https://www.artstation.com/artwork/18mLB8), deren Bilder nicht ausgeliefert werden.
