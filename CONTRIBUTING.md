# Contributing

Thanks for your interest in the BlackICE Server Panel!

## Pull requests

- Open an issue first for larger changes so we can agree on the approach.
- Fork the repository, create a branch and open a pull request against `main`; fill in the pull request template.
- **A maintainer must approve every pull request** before it can be merged. Direct pushes to `main` are limited to maintainers, and continuous integration (Windows, Linux, Docker) must pass.
- Workflows from outside contributors only run after a maintainer approves them.
- Keep changes focused. Update English **and** German texts (UI strings in `ui/src/locales.ts`, `README.md` / `README.de.md`, docs) when you change user-facing behavior.

## Local checks

```sh
npm --prefix ui ci
npm --prefix ui run check
npm --prefix ui run build
dotnet run --project tests/Core.Tests
```

See [docs/development.md](docs/development.md) and [docs/building.md](docs/building.md) for the full workflow and the integration tests.

## Security

Please do not report vulnerabilities in public issues. See [SECURITY.md](SECURITY.md).
