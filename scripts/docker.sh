#!/bin/sh
set -eu
root="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
version="$(cat "$root/VERSION")"
case "$version" in *[!0-9a-z.-]*|'') echo 'Invalid release version' >&2; exit 1;; esac
docker compose version >/dev/null
archive="$root/blackice-server-panel-docker.tar.gz"
if [ -f "$archive" ]; then docker load --input "$archive"; fi
env_file="$root/.env"
if [ ! -f "$env_file" ]; then
  umask 077
  cp "$root/deploy/compose.env.example" "$env_file"
  gid="$(stat -c %g /var/run/docker.sock)"
  sed -i "s/^DOCKER_GID=.*/DOCKER_GID=$gid/" "$env_file"
fi
sed -i "s/^PANEL_VERSION=.*/PANEL_VERSION=$version/" "$env_file"
docker compose --env-file "$env_file" -f "$root/deploy/compose.yaml" up -d
docker compose --env-file "$env_file" -f "$root/deploy/compose.yaml" logs --tail=15 supervisor
printf '\nOpen http://127.0.0.1:20772 and use the setup key above.\n'
