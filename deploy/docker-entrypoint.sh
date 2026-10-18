#!/bin/sh
set -eu
role="${1:-web}"
if [ "$#" -gt 0 ]; then shift; fi
state="$(dirname "${REPLAY_PANEL_CONFIG:-/state/panel.json}")"
root=/app
if [ -f "$state/panel-active-runtime" ]; then
  id="$(cat "$state/panel-active-runtime")"
  case "$id" in *[!a-f0-9]*|'') echo 'Invalid panel runtime selection' >&2; exit 1;; esac
  [ "${#id}" -eq 32 ] || exit 1
  root="$state/panel-updates/$id/stage"
fi
case "$role" in
  web) cd "$root/artifacts/web"; exec ./Web "$@";;
  supervisor) exec "$root/artifacts/supervisor/Supervisor" "$@";;
  *) echo 'Unknown panel role' >&2; exit 1;;
esac
