FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
RUN apt-get update && apt-get install -y gcc libc6-dev
WORKDIR /test
COPY global.json Directory.Build.props ./
COPY src src
COPY tests tests
COPY native native
RUN dotnet publish tests/ConsoleFixture -c Release -r linux-x64 --self-contained true -o /fixture && dotnet publish tests/Backend.Tests -c Release -r linux-x64 --self-contained true -o /runner && cc -O2 native/terminal.c -lutil -o /runner/replay-terminal
FROM debian:trixie-slim
RUN apt-get update && apt-get install -y ca-certificates libicu76 libssl3t64 libstdc++6 zlib1g && rm -rf /var/lib/apt/lists/*
COPY --from=build /fixture /fixture
COPY --from=build /runner /runner
ENTRYPOINT ["/runner/Backend.Tests", "/fixture"]
