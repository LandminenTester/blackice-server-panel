# BlackICE Server Panel

Unofficial dashboard for one **BlackICE / REServer** instance: server controls, live console and logs, bundle management, multiple users with individual permissions, English/German localization, protected setup, official server installation and a Cyberpunk-inspired interface.

[Deutsch](README.de.md) · [Release 1.0](https://github.com/LandminenTester/blackice-server-panel/releases/tag/v1.0.0)

## Windows

1. Download `blackice-server-panel-win-x64.zip` and extract into a writable folder **separate from the game server**.
2. Open `Start.exe` and keep its control terminal open.
3. Open http://127.0.0.1:20772. Read the setup key from the terminal or `.local/supervisor.log`, create your administrator and choose an existing server or an empty directory for the official download.

Windows x86-64 is required; the .NET runtime is included. No SDK or Node.js is needed. The game opens a separate console. Closing it stops the game; closing Start, Ctrl+C or Q stops the whole stack. Automatic game startup is configurable under Settings.

## Native Linux

Debian 13 x86-64 (glibc) is the reference platform. ARM and Alpine/musl are unsupported. Download `blackice-server-panel-linux-x64.tar.gz`, then:

```sh
sudo apt update
sudo apt install ca-certificates libicu76 libssl3t64 libstdc++6 zlib1g tmux
mkdir -p ~/blackice-panel
tar -xzf blackice-server-panel-linux-x64.tar.gz -C ~/blackice-panel
cd ~/blackice-panel
chmod +x Start
./Start
```

Complete setup at localhost:20772 as above. .NET and the PTY helper are included; the official server installer requires no Docker daemon. Over SSH the game console uses tmux: C attaches, Ctrl+B then D detaches. Desktop users should install `xterm` for separate windows. [Services and library checks](docs/linux.md).

## Docker on Linux

Requires x86-64 Linux, Docker Engine and Compose v2. Extract a native release package for `deploy/`, and download `blackice-server-panel-docker.tar.gz` from the same release.

```sh
gzip -dc blackice-server-panel-docker.tar.gz | docker load
cp deploy/compose.env.example .env
sudo mkdir -p /srv/blackice-panel/state /srv/blackice-panel/runtime /srv/blackice-server
sudo chown 10001:10001 /srv/blackice-panel/state /srv/blackice-panel/runtime /srv/blackice-server
sudo chmod 700 /srv/blackice-panel/state /srv/blackice-panel/runtime /srv/blackice-server
stat -c %g /var/run/docker.sock
```

Edit `.env`: set `PANEL_STATE=/srv/blackice-panel/state`, `PANEL_RUNTIME=/srv/blackice-panel/runtime`, `SERVER_PATH=/srv/blackice-server`, `DOCKER_GID` to the printed socket group ID, and keep `PANEL_VERSION=1.0.0`. Then:

```sh
docker compose --env-file .env -f deploy/compose.yaml up -d
docker compose --env-file .env -f deploy/compose.yaml logs supervisor
```

Open localhost:20772, use the logged setup key, select Docker and `/server`. Only the Supervisor receives the powerful Docker socket; Webhost does not. Game files and panel state persist in host directories. Stop with `docker compose --env-file .env -f deploy/compose.yaml down`. [Docker details](docs/docker.md).

## Operation and updates

- Header: status, Start, Restart and Stop. Graceful Stop waits up to 30 seconds; Force Stop is separate. Bundle actions wait for server confirmation.
- Users: individual rights for server controls, console, logs, bundles, settings and account administration. Give console-write access only to trusted operators.
- Settings → Panel updates: stable GitHub releases, SHA-256 verification, installation/restart, backup and startup-failure rollback. Configuration, accounts and game files remain. Native Start installations support installation; Docker/systemd use deployment tools. **0.5.1 → 1.0 needs one manual package replacement following the repository rename; preserve `.local/`.** [Updates and recovery](docs/updates.md).
- Closing a browser tab stops nothing. `keep-running` / `stop-server` controls Webhost shutdown; closing Start always stops the whole stack. Externally started servers are detected, preventing duplicate starts; their console cannot be taken over.
- Ports: game/bundle downloads 20770 TCP/UDP, voice 20771 TCP/UDP, dashboard 20772 TCP on localhost. For remote access use the documented [HTTPS proxy settings](docs/operations.md).
- Back up `.local/` and your game-server directory. New server downloads use LAN mode and disabled automatic server updates. Public game servers require [official registration](https://docs.replay.re/servers/registering/).

## Development

.NET SDK 10 and Node.js 24; Linux also requires a C compiler and tmux. [Exact build commands and troubleshooting](docs/building.md) · [Architecture and development](docs/development.md) · [Permissions](docs/users.md). Windows/Linux CI verifies backends, authentication, setup, bundle operations, launcher shutdown and update rollback. Docker CI exercises real server installation.

## Links and credits

[BlackICE by REPLAY.re](https://replay.re/) · [Official docs](https://docs.replay.re/) · [Official account/server panel](https://panel.replay.re/) · [Forum](https://forum.replay.re/)

MIT community project; not affiliated with or endorsed by REPLAY.re, BlackICE or CD PROJEKT RED. Original Cyberpunk-inspired UI; [visual reference](https://www.artstation.com/artwork/18mLB8), whose artwork is not distributed.
