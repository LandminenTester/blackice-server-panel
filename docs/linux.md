# Native Linux setup / Einrichtung

## English

Reference: Debian 13 (trixie), x86-64. Other glibc distributions require a library check; musl/Alpine and ARM are not supported by these packages.

`sudo apt install ca-certificates libicu76 libssl3t64 libstdc++6 zlib1g`

Extract the Linux package, then run `sh scripts/start.sh`. .NET and the PTY helper are included. Read `.local/supervisor.log` for the setup key and open localhost:20772. Choose Linux and an existing installation or empty target directory.

The OCI installer accesses the official registry directly, selects linux/amd64, verifies layer SHA-256 digests and extracts only `/opt/replay/seed`. A Docker daemon is not required. Existing installations need executable `REServer`, `cfg/server.cfg` and write access. Run `LD_LIBRARY_PATH=/srv/replay ldd /srv/replay/REServer`; no library may be missing. The official bundled libstdc++ is used through the backend's library path.

For systemd create user `replay-panel` and private directories `/var/lib/replay-panel` and `/srv/replay` owned by that account (0700). Extract the package under `/opt/replay-panel`. Install `deploy/replay-panel-*.service` into `/etc/systemd/system`, then run:

```sh
sudo systemctl daemon-reload
sudo systemctl enable --now replay-panel-supervisor replay-panel-web
sudo journalctl -u replay-panel-supervisor -n 30
```

Both services use the same UID and runtime path. Socket/config mode is 0600 and the runtime directory is 0700. Stopping only the web service exercises the chosen shutdown mode. Stopping the Supervisor stops its game server.

## Deutsch

Referenz: Debian 13 (trixie), x86-64. Andere glibc-Systeme benötigen eine Bibliotheksprüfung. musl/Alpine und ARM werden nicht unterstützt.

`sudo apt install ca-certificates libicu76 libssl3t64 libstdc++6 zlib1g`

Paket entpacken und `sh scripts/start.sh` ausführen. .NET und PTY-Helfer sind enthalten. Schlüssel aus `.local/supervisor.log` lesen und localhost:20772 öffnen. Linux sowie vorhandene Installation oder leeren Zielordner wählen.

Der OCI-Installer greift direkt auf die offizielle Registry zu, wählt linux/amd64, prüft SHA-256 und extrahiert nur `/opt/replay/seed`. Ein Docker-Daemon ist nicht erforderlich. Vorhandene Server benötigen ausführbares `REServer`, `cfg/server.cfg` und Schreibrechte. `LD_LIBRARY_PATH=/srv/replay ldd /srv/replay/REServer` darf keine fehlende Bibliothek melden.

Für systemd Benutzer `replay-panel` anlegen; `/var/lib/replay-panel` und `/srv/replay` diesem Konto mit 0700 zuordnen. Paket nach `/opt/replay-panel` entpacken, Dienstdateien aus `deploy` nach `/etc/systemd/system` kopieren und die obigen systemctl-Befehle ausführen. Beide Dienste müssen dieselbe UID und denselben Laufzeitpfad nutzen. Ein Stop des Webdiensts berücksichtigt den Abschaltmodus; ein Stop des Supervisors beendet dessen Spielserver.
