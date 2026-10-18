#!/bin/sh
set -eu
for directory in /state /run/replay-panel; do
  [ ! -L "$directory" ] || exit 1
  mkdir -p "$directory"
  find "$directory" -xdev \( -type d -o -type f \) -exec chown 10001:10001 {} + -exec chmod u+rwX,go-rwx {} +
done
exec /app/docker-permissions.sh
