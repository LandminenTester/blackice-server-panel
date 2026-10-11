# Docker setup / Einrichtung

## English

Linux x86-64, Docker Engine and Compose v2. Webhost and Supervisor are separate; only the Supervisor receives the Docker socket.

First download the versioned Docker archive from the public GitHub release and load it with `gzip -dc blackice-server-panel-docker.tar.gz | docker load`. This installs the exact image tag referenced by Compose without requiring a registry login.

1. Copy `deploy/compose.env.example` to private `.env`. Set absolute `PANEL_STATE`, `PANEL_RUNTIME` and `SERVER_PATH`. Set `DOCKER_GID` from `stat -c %g /var/run/docker.sock`.
2. Create these host directories, owned by UID/GID 10001 with mode 0700. The server directory must be empty for installation.
3. Run `docker compose --env-file .env -f deploy/compose.yaml up -d`.
4. Get the setup key using `docker compose --env-file .env -f deploy/compose.yaml logs supervisor` and open localhost:20772.
5. Select Docker, official download and the mounted `/server` directory.

The Supervisor pulls `ghcr.io/replayre/server`, creates an owned container with TTY/stdin, persistent storage, LAN configuration and disabled auto-updates. It verifies the full container ID and ownership label before control. `REPLAY_PANEL_HOST_SERVER` is the daemon-side bind path and must match your host directory. Game/voice use 20770/20771 TCP+UDP.

Build locally with `docker build -t replay-panel:local .` and replace both image fields in a private Compose copy. Use fixed release versions. The Docker socket gives the Supervisor powerful host access; keep it private and never mount it into Webhost. Both services use UID 10001; only the Supervisor additionally joins the socket group.

Stop only Webhost using `docker compose ... stop web` for keep-running behavior. A Supervisor stop also stops its game server. Back up host server files and private state.

## Deutsch

Linux x86-64 mit Docker Engine und Compose v2. Webhost und Supervisor sind getrennt; nur der Supervisor erhält den Docker-Socket.

Zuerst das versionierte Docker-Archiv aus dem öffentlichen GitHub-Release laden und mit `gzip -dc blackice-server-panel-docker.tar.gz | docker load` importieren. Damit liegt das passende Compose-Image ohne Registryanmeldung lokal vor.

1. `deploy/compose.env.example` als private `.env` kopieren. Absolute Pfade für `PANEL_STATE`, `PANEL_RUNTIME` und `SERVER_PATH` festlegen. `DOCKER_GID` mit `stat -c %g /var/run/docker.sock` ermitteln.
2. Hostordner anlegen, UID/GID 10001 und 0700 zuordnen. Serverordner für Installation leer lassen.
3. `docker compose --env-file .env -f deploy/compose.yaml up -d` ausführen.
4. Schlüssel mit `docker compose --env-file .env -f deploy/compose.yaml logs supervisor` lesen und localhost:20772 öffnen.
5. Docker, offiziellen Download und eingebundenen Ordner `/server` wählen.

Der Supervisor lädt `ghcr.io/replayre/server` und erstellt seinen Container mit TTY/stdin, dauerhaftem Speicher, LAN-Konfiguration und deaktivierten Updates. Vor Steuerungsaktionen werden vollständige ID und Zugehörigkeitslabel geprüft. `REPLAY_PANEL_HOST_SERVER` muss dem Pfad auf dem Dockerhost entsprechen. Spiel/Voice nutzen 20770/20771 TCP und UDP.

Lokal mit `docker build -t replay-panel:local .` bauen und beide Imagefelder in einer privaten Compose-Kopie ersetzen. Feste Releaseversionen verwenden. Der Docker-Socket erlaubt dem Supervisor weitreichende Hostzugriffe; intern halten und niemals dem Webhost zuweisen. Beide Dienste verwenden UID 10001, nur der Supervisor bekommt zusätzlich die Socketgruppe.

Für keep-running nur `docker compose ... stop web` ausführen. Supervisor-Stop beendet auch den Spielserver. Host-Serverdateien und privaten Panelzustand sichern.
