FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /build
COPY global.json Directory.Build.props ./
COPY src src
COPY tests/DockerUpdates.Tests tests/DockerUpdates.Tests
RUN dotnet publish tests/DockerUpdates.Tests -c Release -r linux-x64 --self-contained true -o /runner
FROM replay-panel:next
COPY --from=build /runner /test-runner
