# Docker setup and updates / Einrichtung und Updates

## English

Linux x86-64, Docker Engine and Compose v2 are required. Extract `blackice-server-panel-docker-bundle.tar.gz` from the public release and run `sh scripts/docker.sh`. It contains the matching image, version, Compose file and scripts. No registry login or native package is needed. An existing private `.env` is preserved; on first run one is created and the socket group detected. Edit `.env` first for custom absolute host paths.

Compose runs a network-isolated initializer to prepare private state/runtime ownership and named-user/default ACLs on the server's configuration and bundle directories. Webhost and Supervisor then run as UID 10001; only Supervisor joins the socket group and receives the Docker socket. The initializer never receives the socket. Supervisor repairs its labelled game's server-mount permissions on startup, after game start and before config saves. Symlinks are not traversed and no world-write permission is added. Host storage must support POSIX ACLs; read-only mounts and unsupported filesystems fail explicitly.

Open http://127.0.0.1:20772, use the logged setup key, create your administrator and select Docker with `/server`. Fresh downloads require no existing runtime. The prepared empty `cfg`, `settings` and `data/ugc` directories are accepted. Game/voice defaults are 20770/20771 TCP+UDP. Remote panel access uses the documented HTTPS proxy configuration.

### Updates and migration

Game updates use Settings → REServer updates. Panel-managed game startup preserves `settings/server.settings.json`; settings take effect after restarting the game through the panel. Adopting an older game container's entrypoint requires its next panel-managed start and may recreate that stopped game container while preserving mounts and networks.

Panel updates use Settings → Panel updates, with stable/experimental channels. Download and verify the release, then install. Docker stores the verified Linux runtime in `state/panel-updates/<job-id>/stage` and selects it with `state/panel-active-runtime`. A temporary helper restarts only this deployment's Webhost and Supervisor, verifies version and HTTP readiness, and restores the previous selection on failure. It mounts private state, runtime and the Docker socket, **never the game directory**. The game remains running. Finish panel jobs before installing an update.

The active runtime survives container recreation and Portainer/Compose redeployment. The base image tag identifies the OS/bootstrap image; the dashboard reports the active panel version. Base-image/OS upgrades remain deployment operations. Back up panel state and game files; retain previous runtime directories for rollback.

Existing deployments need a one-time migration: both services use `/app/docker-entrypoint.sh` with role `web` or `supervisor`; set their `replay.panel.role` labels; add `REPLAY_PANEL_DOCKER_UPDATES=1` to Supervisor; share the same private state/runtime mounts and Compose project. Keep Supervisor non-root and Webhost socket-free. These checks prevent selecting unrelated containers. Account permissions and CSRF remain enforced.

Recovery: stop both panel services and retain `state/panel-updates`. Set `state/panel-active-runtime` to a previous verified job ID, or remove only the marker to use the base image's runtime. Start both services. Inspect `state/panel-updates/<job-id>/apply.log` and `state/panel-update.json` for failures. Never delete the runtime directory selected by the active marker.

## Deutsch

Das eigenständige Docker-Paket enthält Image, Compose-Datei und Startskript. Nach dem Entpacken `sh scripts/docker.sh` ausführen. Eine bestehende private `.env` bleibt erhalten; bei der Ersteinrichtung werden Standardpfade und Socketgruppe vorbereitet. Hostpfade vorher bei Bedarf anpassen. Der Initialisierer bereitet Dateirechte vor, Supervisor und Webhost laufen anschließend unter UID 10001. Nur der Supervisor erhält den Docker-Socket. POSIX-ACLs werden benötigt; Symlinks werden nicht verfolgt und keine Schreibrechte für alle gesetzt.

Spielserver- und Panel-Updates sind im Dashboard verfügbar. Panel-Updates prüfen das Linux-Release, speichern die Laufzeit im gemeinsamen Zustand und starten ausschließlich die beiden Panel-Dienste neu. Der Spielserver bleibt aktiv. Bei Startfehlern wird die vorige Panel-Laufzeit wieder ausgewählt. Container-IDs, Netzwerke und die Portainer-/Compose-Definition bleiben erhalten. Das Basisimage aktualisiert weiterhin das Bereitstellungswerkzeug; die aktive Panel-Laufzeit bleibt im Zustand gespeichert.

Bestehende Installationen einmal auf die mitgelieferten Einstiegspunkte, Rollenlabels, gemeinsamen Mounts und `REPLAY_PANEL_DOCKER_UPDATES=1` umstellen. Zur Wiederherstellung beide Panel-Dienste stoppen und `panel-active-runtime` im privaten Zustand auf die vorige geprüfte Job-ID setzen oder nur diese Datei entfernen, um die Basisimage-Laufzeit zu starten. Zustands- und Spielserverdateien sichern; aktive Laufzeitordner nicht löschen. Kontoberechtigungen bleiben unverändert.
