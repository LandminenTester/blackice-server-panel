# Replay Server Panel

A dashboard for one Replay server: server controls, live console, logs and bundle management. English and German, a protected setup assistant, and an original Cyberpunk-inspired interface.

[Deutsch](README.de.md) · [Windows](docs/windows.md) · [Linux](docs/linux.md) · [Docker](docs/docker.md) · [Development](docs/development.md) · [Build guide](docs/building.md) · [Operations](docs/operations.md)

## Quick start

Download a [release package](https://github.com/LandminenTester/replay-server-panel/releases). Windows and Linux packages include the .NET runtime. Node.js is only needed for development.

- Windows: extract separately from the server directory and open `Start.exe`.
- Native Linux: Debian 13 x86-64 is the reference platform. Install the documented system libraries, then run `./Start`.
- Docker: use the [Compose deployment](docs/docker.md).

Open `http://127.0.0.1:20772`. Read the setup key from `.local/supervisor.log` or the Supervisor service logs. Choose a dashboard name, username, password and server folder. Existing installations are preserved; the official installer requires an empty target directory.

## Server operation

Normal stop requests graceful exit and waits up to 30 seconds. Force stop is a separate confirmed action. Bundle operations wait for an actual Replay lifecycle confirmation. An externally started server is detected and cannot be started twice or taken over.

`keep-running` preserves the game server when the Webhost exits; `stop-server` requests a graceful stop. Closing a browser tab does not stop anything. Windows uses ConPTY, native Linux a real PTY, and Docker the scoped Engine API with TTY and stdin.

New downloads use LAN mode and disabled automatic updates. For a public game server, follow [Replay registration](https://docs.replay.re/servers/registering/) and set its private key in `cfg/server.cfg`. [Official installation reference](https://docs.replay.re/servers/installation/).

Existing v1 configurations are backed up to `panel.json.v1.bak` and migrated, preserving password hash, server path and shutdown mode. The existing login gets username `admin`.

| Service | Port |
|---|---|
| Game / bundle download | 20770 TCP/UDP |
| Voice | 20771 TCP/UDP |
| Dashboard | 20772 TCP, localhost |
| Private IPC | Named pipe / Unix socket |

Remote access uses an HTTPS reverse proxy with explicit trusted proxies, hosts and secure cookies; see [Operations](docs/operations.md).

## Source

`src/Supervisor` owns installation, backends and bundle protocol. `src/Web` hosts authenticated HTTP and static UI. `src/Shared` owns configuration and IPC. `ui` contains Svelte and typed locales. `native/terminal.c` bridges Linux PTYs.

MIT. Community tool, not an official Replay or Cyberpunk product. [Design reference](https://www.artstation.com/artwork/18mLB8); its artwork is not distributed.

The direct Start entry keeps a control terminal open. Closing it, Ctrl+C, or Q requests complete graceful shutdown. A separate native server terminal supports Replay commands and closes the game when closed. Auto-start is configured under Settings. Docker uses Compose instead.
