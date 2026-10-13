# Bundle tools (Experimental)

Optional, **off by default**, shipped only in the experimental update channel (Settings → Updates). Enable in Settings → Bundle tools or in the setup wizard.

## What it does
- **Upload**: upload a finished bundle ZIP. Only the structure is checked (`config.toml`, referenced files, no path traversal, no nested manifests). The previous version is backed up.
- **Toolchain**: the panel installs git (Windows), .NET SDK, WASI SDK and Node into `<state>/toolchain`. No CLI needed.
- **Projects**: create a project from a C# template (optional ReUi with Svelte, Vue or React, TypeScript, router, Pinia/Zustand, Tailwind), or import a Git repository (GitHub, GitLab, Gitea/Forgejo/Codeberg, public or private via encrypted tokens). Optionally create the remote repository (private or public).
- **Build**: Pull, Build or Pull & Build. Output lands in `<server>/data/ugc/<bundle>`, sources in `<server>/panel_src/<bundle>`. Rollback restores the previous build.
- **Packages**: view and set the `replay.blackice.*` NuGet version. Never automatic.

Nothing updates by itself: no scheduled pulls, builds or package updates.

## Docker
Supported. The image contains `git`; toolchain data lives in the `/state` volume, bundles are written to the `/server` mount. CI runs the full flow in the container.

## Security
Builds run project code on the host. See [SECURITY.md](../SECURITY.md).

---

# Bundle-Tools (Experimentell)

Optional, **standardmäßig aus**, nur im experimentellen Update-Kanal (Einstellungen → Updates). Aktivierung unter Einstellungen → Bundle-Tools oder im Setup-Assistenten.

- **Upload**: fertiges Bundle-ZIP hochladen, nur Strukturprüfung, Backup der alten Version.
- **Toolchain**: git (Windows), .NET SDK, WASI SDK und Node installiert das Panel selbst nach `<state>/toolchain`.
- **Projekte**: neues Projekt aus Vorlage (optional ReUi mit Svelte, Vue oder React) oder Git-Repository importieren (GitHub, GitLab, Gitea/Forgejo/Codeberg), optional Remote-Repo anlegen.
- **Build**: Pull, Build, Pull & Build. Ausgabe in `<server>/data/ugc/<bundle>`, Quellen in `<server>/panel_src/<bundle>`. Rollback möglich.
- **Pakete**: `replay.blackice.*`-Version anzeigen und setzen, nie automatisch.

Es wird nichts automatisch aktualisiert. Docker wird vollständig unterstützt (`git` im Image, Toolchain im `/state`-Volume). Builds führen Projektcode auf dem Host aus, siehe SECURITY.md.
