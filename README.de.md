# Replay Server Panel

Dashboard für einen Replay-Server: Serversteuerung, Live-Konsole, Logs und Bundle-Verwaltung. Deutsch und Englisch, geschützter Einrichtungsassistent und eigenes, von Cyberpunk inspiriertes Design.

[English](README.md) · [Windows](docs/windows.md) · [Linux](docs/linux.md) · [Docker](docs/docker.md) · [Entwicklung](docs/development.md) · [Betrieb](docs/operations.md)

## Einstieg

Ein passendes [Releasepaket](https://github.com/LandminenTester/replay-server-panel/releases) herunterladen. Die .NET-Laufzeit ist enthalten; Node.js wird nur zum Entwickeln benötigt.

- Windows: getrennt vom Serverordner entpacken und `scripts/start.ps1` ausführen.
- Natives Linux: Debian 13 x86-64 ist die Referenzplattform. Dokumentierte Systembibliotheken installieren und `sh scripts/start.sh` ausführen.
- Docker: die [Compose-Bereitstellung](docs/docker.md) verwenden.

`http://127.0.0.1:20772` öffnen. Einrichtungsschlüssel aus `.local/supervisor.log` oder dem Supervisor-Dienstlog lesen. Dashboardname, Benutzername, Passwort und Serverordner festlegen. Vorhandene Installationen bleiben erhalten; der offizielle Download benötigt einen leeren Zielordner.

## Betrieb

Normaler Stop fordert geordnetes Beenden an und wartet bis zu 30 Sekunden. Erzwungener Stop ist eine getrennte Aktion mit Bestätigung. Bundle-Aktionen warten auf ein tatsächliches Replay-Ereignis. Ein extern gestarteter Server wird erkannt; Doppelstart und Übernahme werden verhindert.

`keep-running` lässt den Spielserver beim Beenden des Webhosts laufen; `stop-server` fordert seinen geordneten Stop an. Das Schließen eines Browser-Tabs stoppt nichts. Windows verwendet ConPTY, Linux eine echte PTY und Docker die eingeschränkte Engine-API mit TTY und stdin.

Neue Downloads verwenden LAN-Modus und deaktivierte automatische Updates. Öffentliche Spielserver benötigen die [Replay-Registrierung](https://docs.replay.re/servers/registering/) und einen privaten Schlüssel in `cfg/server.cfg`. [Offizielle Installationsreferenz](https://docs.replay.re/servers/installation/).

Alte v1-Konfigurationen werden als `panel.json.v1.bak` gesichert und migriert. Passwort-Hash, Serverpfad und Abschaltmodus bleiben erhalten; der bisherige Zugang erhält den Benutzernamen `admin`.

| Dienst | Port |
|---|---|
| Spiel / Bundle-Download | 20770 TCP/UDP |
| Voice | 20771 TCP/UDP |
| Dashboard | 20772 TCP, localhost |
| Private IPC | Named Pipe / Unix-Socket |

Fernzugriff erfolgt über HTTPS-Reverse-Proxy mit ausdrücklich eingetragenen Hosts, vertrauenswürdigen Proxys und geschützten Cookies. Siehe [Betrieb](docs/operations.md).

## Quellen

`src/Supervisor` verwaltet Installation, Backends und Bundle-Protokoll. `src/Web` stellt geschützte HTTP-Endpunkte und UI-Dateien bereit. `src/Shared` enthält Konfiguration und IPC. `ui` enthält Svelte und typisierte Sprachkataloge. `native/terminal.c` verbindet die Linux-PTY.

MIT-Lizenz. Community-Werkzeug, kein offizielles Replay- oder Cyberpunk-Produkt. [Designreferenz](https://www.artstation.com/artwork/18mLB8); deren Bilder werden nicht ausgeliefert.
