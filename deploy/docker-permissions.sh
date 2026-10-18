#!/bin/sh
# Runs in a short-lived helper, with only the owned server directory mounted.
set -eu
root=/server
[ -d "$root" ] && [ ! -L "$root" ] || exit 1
setfacl -m u:10001:rwx,d:u:10001:rwx "$root"
for relative in cfg settings data data/ugc; do
  target="$root/$relative"
  [ ! -L "$target" ] || exit 1
  mkdir -p "$target"
  setfacl -m u:10001:rwx,d:u:10001:rwx "$target"
done
for relative in cfg settings data/ugc; do
  target="$root/$relative"
  # Do not follow links into another mount or add world-write permissions.
  find "$target" -xdev -type d -exec setfacl -m u:10001:rwx,d:u:10001:rwx {} +
  find "$target" -xdev -type f -exec setfacl -m u:10001:rw- {} +
done
