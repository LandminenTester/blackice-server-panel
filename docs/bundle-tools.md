# Bundle tools (Experimental)

Optional, **off by default**, shipped only in the experimental update channel (Settings → Updates). Enable in Settings → Bundle tools or in the setup wizard.

## What it does
- **Upload**: upload a finished bundle ZIP. Only the structure is checked (`config.toml`, referenced files, no path traversal, no nested manifests). The previous version is backed up.
- **Toolchain**: detected host installations of Git, .NET SDK 10 and Node.js with npm can be used directly. The tool list distinguishes system tools from panel-managed copies. Optional private copies are installed into `<state>/toolchain` and take precedence; removing a panel copy preserves the system installation. An existing WASI SDK is reused from `WASI_SDK_PATH` or the user's `.wasi-sdk` cache; otherwise the first build downloads it.
- **Projects**: create a project from a C# template (optional ReUi with Svelte, Vue or React, TypeScript, router, framework-specific state management, Tailwind), or import a Git repository (GitHub, GitLab, Gitea/Forgejo/Codeberg, public or private via encrypted tokens). Optionally create the remote repository (private or public).
- **Build**: Pull, Build or Pull & Build. Output lands in `<server>/data/ugc/<bundle>`, sources in `<server>/panel_src/<bundle>`. Rollback restores the previous build.
- **Packages**: check installed/latest `replay.blackice.*` NuGet versions, then propose the latest or a specific version through an isolated change branch and review request. Active sources and runtime files remain unchanged.

Nothing updates by itself: no scheduled pulls, builds or package updates.

Settings → Bundle tools has three subtabs: **General** for feature/build preferences, **Build tools** for detection and private tool copies, and **Git accounts** for repository credentials. The experimental feature switch remains above them. Use Left/Right or Home/End to navigate the subtabs by keyboard.

Tool discovery runs on the **panel host**, under its account, rather than in the browser. Docker sees tools inside its container, not installations on the Docker host. A service account may therefore see a different PATH or WASI cache from your desktop account. Detection is cached for up to 30 seconds; reopen Build tools after installing a host tool. A .NET runtime without SDK 10, or Node.js without a discoverable npm CLI, is insufficient for bundle builds. Builds keep their own home and package-cache directories even when using system executables.

## Workspace

Projects are searchable compact rows with their last build, branch/commit and spaced UI badges. **Details** opens Project and Merge branches tabs in the two-column workspace for Git refs, package versions, source actions and merge settings. New-project and import dialogs separate project options from web packages or remote discovery. Common optional web packages include Zod, Lucide icons, TanStack Query, clsx, Day.js, Floating UI, Vitest and Prettier; they are pinned template choices, not an arbitrary package installer.

Import can discover repositories through a saved account, owner and paged search, or accept a direct HTTPS clone URL. Creation offers a personal/organization/group owner selector when supported by the configured provider and token.

Upload separates archive metadata/installation from structure checks. The job viewer shows progress and original build output with search, follow, pause/resume and log download. Pause affects viewing; it does not cancel a build. Logs remain technical output and are displayed as text.

## Shared Git accounts and permissions

Accounts configured here belong to the server installation. Use dedicated provider tokens scoped to the repositories the server needs; do not add a broad personal administrator token. Tokens cannot be revealed or downloaded through the dashboard. Managing a token, using it for repository work, and writing changes to a remote are separate permissions; see [the complete permission list](users.md).

Provider access uses HTTPS. The token is bound to the configured provider origin/path and cannot be retained when changing its destination without entering a replacement token. Authenticated provider calls and Git operations do not follow redirects. Provider API discovery can list accessible owners and repositories; available organizations/groups depend on the provider, membership and token permissions. An organization appearing in a list does not guarantee that its policy permits creation or pushing.

## Remote branch workflow

Develop on your own computer and push to the Git provider. On the development server, select the project's tracking branch and use **Pull & Build**. On the main server, use its production branch and update separately after testing.

**Merge branches** accepts independently selected source and target remote branches: feature → dev works just like dev → main. Compare first to see exact commit identities, commits and changed files. Branch creation and local Commit & Push are no longer part of this surface.

The global policy defaults to **Review request**; projects can inherit or override it. **Direct merge** is explicitly selectable and requires its own permission and confirmation. Existing **Branches only** policies remain disabled for merging. Direct merging creates a provider review object and asks the provider to merge it; protections, CI and review requirements can refuse the action. Source and target are checked again before execution. Conflicts require local resolution; there is no force-push, auto-retry, source-branch deletion or automatic deployment. A refused merge may leave a review request at the provider; inspect its link before retrying.

Package checks open a separate dialog with installed and latest common versions. Choose the latest release or a specific version, and the remote branch to update. The panel clones into temporary staging, modifies pinned project references, pushes `panel/packages-<job-id>` and creates a review request. It never changes the active checkout or deploys the result. Without a configured remote and account, checks remain read-only. A failed review request can leave its published change branch; logs name it for recovery.

State choices are React: none/Zustand/Jotai/Nano Stores; Vue: none/Pinia/Nano Stores; Svelte: local runes/shared runes/Nano Stores. Templates include working counter examples with pinned dependencies. Vue + Nano Stores uses `skipLibCheck` for incompatible upstream declarations under TypeScript 6; application code is still type-checked.

## Bundle organization and lifecycle

Search installed bundles by name and filter server-wide category tags. Categories live in panel state and survive bundle updates.

**Disable auto-start** keeps files and current running state intact. Manual start remains possible. The panel comments out exact `start <bundle>` lines in `cfg/server.cfg` and statically included files, preserving original text in reversible markers and backing up changed files. Reenable restores the original commands and order. Disabled entries are reconciled before managed startup; builds do not automatically restart a bundle with disabled auto-start. Dynamic, ambiguous, external or linked configurations are refused. Console operators and external file editors can still change startup behavior outside the panel.

**Delete bundle** has an explicit scope: runtime files, optionally project sources and bundle-specific backups. Data is kept by default. Only an exclusively local, non-linked `vfs` directory can be selected for data deletion; external mounts are never removed. A running bundle must stop with confirmed state first. Deletion is permanent and is not a substitute for backup. Check filesystem access and startup configuration before using it.

ZIP uploads recheck the final target name before installation and ask **Overwrite or Cancel** for a collision, including after renaming. Replacement stops the current bundle, backs up runtime files and preserves data. File structure checks do not establish that uploaded code is trustworthy.

## Docker
Supported. The image contains `git`; toolchain data lives in the `/state` volume, bundles are written to the `/server` mount. CI runs the full flow in the container.

## Security
Builds run project code with the panel OS account's rights and can access that account's files. Disabling install scripts and encrypting saved tokens do not sandbox untrusted code. Grant build access only to trusted developers and isolate development from production. See [SECURITY.md](../SECURITY.md).

---

# Bundle-Tools (Experimentell)

Optional, **standardmäßig aus**, nur im experimentellen Update-Kanal (Einstellungen → Updates). Aktivierung unter Einstellungen → Bundle-Tools oder im Setup-Assistenten.

- **Upload**: fertiges Bundle-ZIP hochladen, nur Strukturprüfung, Backup der alten Version.
- **Toolchain**: erkannte Host-Installationen von Git, .NET SDK 10 und Node.js mit npm können direkt verwendet werden. Die Anzeige unterscheidet Systemwerkzeuge und Panel-Kopien. Optionale private Kopien liegen unter `<state>/toolchain` und haben Vorrang; ihr Entfernen erhält die Systeminstallation. Ein vorhandenes WASI SDK wird aus `WASI_SDK_PATH` oder dem `.wasi-sdk`-Cache des Benutzers wiederverwendet, andernfalls beim ersten Build heruntergeladen.
- **Projekte**: neues Projekt aus Vorlage (optional ReUi mit Svelte, Vue oder React) oder Git-Repository importieren (GitHub, GitLab, Gitea/Forgejo/Codeberg), optional Remote-Repo anlegen.
- **Build**: Pull, Build, Pull & Build. Ausgabe in `<server>/data/ugc/<bundle>`, Quellen in `<server>/panel_src/<bundle>`. Rollback möglich.
- **Pakete**: installierte und verfügbare `replay.blackice.*`-Versionen prüfen; Updates über einen isolierten Änderungsbranch und eine Review-Anfrage vorschlagen.

## Arbeitsbereich

Projekte erscheinen als durchsuchbare kompakte Zeilen mit letztem Build, Branch-/Commit-Stand und einheitlichen UI-Badges. **Details** öffnet den zweispaltigen Bereich für Git-Refs, Paketversionen, Quellen und Branch-Merges. Erstellen und Importieren trennen Projektoptionen von Web-Paketen beziehungsweise Remotesuche. Optionale Vorlagenpakete: Zod, Lucide, TanStack Query, clsx, Day.js, Floating UI, Vitest und Prettier; fest gebundene Auswahl statt beliebiger Paketinstallation.

Import über gespeichertes Konto, Eigentümer und seitenweise Repositorysuche oder direkte HTTPS-Clone-Adresse. Beim Erstellen lässt sich ein persönlicher/Organisations-/Gruppeneigentümer wählen, soweit Provider und Token das erlauben. Upload trennt Metadaten und Installation von Strukturprüfungen. Jobausgabe bietet Suche, Folgen, Pause/Fortsetzen und Logdownload. Pause hält nur die Ansicht an; Builds laufen weiter. Originalausgabe bleibt reiner Text.

## Gemeinsame Git-Konten und Berechtigungen

Git-Konten gelten für die Serverinstallation. Eigene, eng auf die benötigten Repositories begrenzte Tokens verwenden. Tokens lassen sich im Dashboard weder anzeigen noch herunterladen. Verwaltung, Verwendung und Remote-Schreibzugriff sind getrennte Rechte; siehe [Berechtigungen](users.md).

Providerzugriff verwendet HTTPS. Tokens sind an die konfigurierte Provider-Adresse samt Pfad gebunden; bei einer Änderung des Ziels ist ein neuer Token nötig. Authentifizierte Provideraufrufe und Git folgen keinen Weiterleitungen. Verfügbare Organisationen/Gruppen und Repositories hängen von Mitgliedschaft, Provider und Tokenrechten ab; die endgültige Schreibberechtigung prüft der Provider.

## Branches zusammenführen und Paketupdates

Entwickler arbeiten auf ihren Rechnern und pushen Änderungen zum Git-Anbieter. Auf dem Entwicklungsserver wird der konfigurierte Branch mit **Pull & Build** aktualisiert; der Hauptserver nutzt seinen Produktionsbranch und wird separat bereitgestellt.

**Branches zusammenführen** bietet Quell- und Zielbranch aus der Remote-Auswahl. Vor der Aktion erscheinen Commit-Stände und Änderungen. Global gilt zunächst **Review-Anfrage**; **Direkter Merge** kann global oder pro Projekt erlaubt werden. Die alte Regel **Nur Branches** bleibt ohne Merge-Erlaubnis erhalten. Direkte Merges benötigen ein eigenes Recht und eine Bestätigung; der Anbieter prüft Branchschutz, Reviews und CI. Konflikte werden außerhalb des Panels gelöst. Kein Force-Push, automatischer Wiederholungsversuch, Branchlöschen oder Deployment. Eine abgelehnte Merge-Aktion kann eine offene Anfrage beim Anbieter hinterlassen.

Der Paketdialog prüft installierte und neueste gemeinsame Versionen. Aktuelle oder bestimmte Version samt Remote-Branch auswählen. Das Panel erstellt in einem temporären Checkout einen Änderungsbranch und eine Review-Anfrage; aktive Quellen und Bundle-Dateien bleiben unverändert. Ohne Remote und passenden Zugang ist die Prüfung nur lesend. Bei einem Fehler nach dem Push kann der Änderungsbranch beim Anbieter verbleiben.

Zustandsverwaltung: React ohne Zusatzbibliothek/Zustand/Jotai/Nano Stores; Vue ohne Zusatzbibliothek/Pinia/Nano Stores; Svelte lokale Runes/gemeinsamer Runes-Store/Nano Stores. Beispiele und Abhängigkeiten sind fest gebunden. Vue mit Nano Stores überspringt per `skipLibCheck` inkompatible Bibliotheksdeklarationen unter TypeScript 6; eigener Anwendungscode wird weiterhin geprüft.

## Kategorien, Autostart und Löschen

Bundle-Suche und Kategorienfilter teilen sich eine Werkzeugleiste mit dem Prüfzeitpunkt. Kategorien werden im Panelzustand gespeichert und überstehen Bundle-Updates.

**Autostart deaktivieren** erhält Dateien und laufenden Zustand. Manuelles Starten bleibt möglich. Exakte `start <bundle>`-Zeilen in der Startkonfiguration und statischen Includes werden gesichert und mit wiederherstellbaren Markierungen deaktiviert. Beim erneuten Aktivieren bleiben Text und Reihenfolge erhalten. Neue Startzeilen werden vor dem verwalteten Serverstart geprüft; deaktivierte Bundles werden nach einem Build nicht automatisch gestartet. Unklare, dynamische, externe oder verlinkte Konfigurationen werden abgelehnt. Konsolenberechtigte und externe Dateiänderungen können diese Regeln weiterhin außerhalb des Panels beeinflussen.

**Bundle löschen** verlangt eine ausdrückliche Umfangswahl und ein bestätigtes Stoppen. Standardmäßig verschwinden nur Laufzeitdateien; Projektquellen, Backups und persistente Daten bleiben. Quellen und eindeutig zugeordnete Backups lassen sich zusätzlich wählen. Datenlöschung wird nur für das exklusive lokale, nicht verlinkte Bundle-VFS angeboten; externe Mounts werden nicht entfernt. Gelöschte Dateien sind nicht über das Panel wiederherstellbar.

ZIP-Uploads prüfen den endgültigen Zielnamen erneut und fragen bei Kollisionen **Überschreiben oder Abbrechen**. Der Austausch stoppt das Bundle, sichert Laufzeitdateien und erhält Daten. Strukturprüfung bedeutet keine Sicherheitsprüfung des enthaltenen Codes.

Es wird nichts automatisch aktualisiert. Docker wird vollständig unterstützt (`git` im Image, Toolchain im `/state`-Volume). Builds führen Projektcode auf dem Host aus, siehe SECURITY.md.

Einstellungen → Bundle-Tools enthält drei Untertabs: **Allgemein** für Funktions- und Build-Einstellungen, **Build-Werkzeuge** für Erkennung und private Werkzeugkopien sowie **Git-Konten** für Repository-Zugänge. Der Schalter für die experimentelle Funktion bleibt darüber sichtbar. Die Untertabs unterstützen Links/Rechts sowie Pos1/Ende auf der Tastatur.

Die Erkennung läuft auf dem **Panel-Host** unter dessen Benutzerkonto, nicht im Browser. Docker erkennt Werkzeuge im Container, nicht auf dem Docker-Host. Ein Dienstkonto kann deshalb einen anderen PATH und WASI-Cache als das Desktop-Konto sehen. Die Erkennung wird bis zu 30 Sekunden zwischengespeichert; nach einer Installation den Untertab Build-Werkzeuge erneut öffnen. Eine .NET-Runtime ohne SDK 10 beziehungsweise Node.js ohne auffindbare npm-CLI reichen für Bundle-Builds nicht aus. Builds verwenden weiterhin eigene Home- und Paketcache-Verzeichnisse, auch mit Systemprogrammen.
