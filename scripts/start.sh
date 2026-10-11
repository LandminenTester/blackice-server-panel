#!/bin/sh
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
umask 077
mkdir -p "$ROOT/.local"
CONFIG="$ROOT/.local/panel.json"
export REPLAY_PANEL_RUNTIME="$ROOT/.local/run"
export REPLAY_PANEL_CONFIG="$CONFIG"
if [ ! -S "$REPLAY_PANEL_RUNTIME/replay-panel.sock" ]; then
 nohup "$ROOT/artifacts/supervisor/Supervisor" "$CONFIG" >"$ROOT/.local/supervisor.log" 2>"$ROOT/.local/supervisor.err.log" </dev/null &
 sleep 1
fi
cd "$ROOT/artifacts/web"
nohup ./Web >"$ROOT/.local/web.log" 2>"$ROOT/.local/web.err.log" </dev/null &
printf 'Panel: http://127.0.0.1:20772\nSetup key: %s/.local/supervisor.log\n' "$ROOT"
