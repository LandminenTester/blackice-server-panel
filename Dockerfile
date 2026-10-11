FROM node:24-bookworm-slim AS ui
WORKDIR /build
COPY ui/package*.json ui/
RUN npm --prefix ui ci
COPY ui ui
RUN npm --prefix ui run check && npm --prefix ui run build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
RUN apt-get update && apt-get install -y --no-install-recommends gcc libc6-dev && rm -rf /var/lib/apt/lists/*
WORKDIR /build
COPY global.json Directory.Build.props ./
COPY src src
COPY native native
COPY --from=ui /build/src/Web/wwwroot src/Web/wwwroot
COPY licenses src/Web/wwwroot/licenses
RUN dotnet publish src/Web -c Release -r linux-x64 --self-contained true -o /out/web && dotnet publish src/Supervisor -c Release -r linux-x64 --self-contained true -o /out/supervisor && cc -O2 -Wall -Wextra native/terminal.c -lutil -o /out/supervisor/replay-terminal
FROM debian:trixie-slim AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates libicu76 libssl3t64 libstdc++6 zlib1g && rm -rf /var/lib/apt/lists/* && useradd -u 10001 -m panel
LABEL org.opencontainers.image.source="https://github.com/LandminenTester/replay-server-panel"
WORKDIR /app
COPY --from=build /out /app/artifacts
ENV REPLAY_PANEL_CONFIG=/state/panel.json REPLAY_PANEL_RUNTIME=/run/replay-panel REPLAY_PANEL_MOUNTS=/server
USER 10001:10001
EXPOSE 20772
ENTRYPOINT ["/app/artifacts/web/Web"]
