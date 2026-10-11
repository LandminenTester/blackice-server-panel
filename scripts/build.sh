#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
npm --prefix ui ci
npm --prefix ui run check
npm --prefix ui run build
cp -R licenses src/Web/wwwroot/
dotnet publish src/Supervisor -c Release -r linux-x64 --self-contained true -o artifacts/supervisor
dotnet publish src/Web -c Release -r linux-x64 --self-contained true -o artifacts/web
cc -O2 -Wall -Wextra native/terminal.c -lutil -o artifacts/supervisor/replay-terminal
dotnet publish src/Supervisor -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .local/launcher
cp .local/launcher/Supervisor Start
chmod +x Start

sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' Directory.Build.props > VERSION
