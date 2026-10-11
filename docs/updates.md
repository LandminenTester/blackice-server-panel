# Dashboard updates / Updates über das Dashboard

## English

In **Settings → Panel updates**, the panel checks the latest stable public release from LandminenTester/blackice-server-panel. It displays installed/latest versions and release notes. Checks are cached for one minute; no GitHub token is required. Drafts and prereleases are excluded.

Choose **Download and verify**. The supervisor downloads the exact Windows x64 ZIP or Linux x64 tar.gz, verifies length and GitHub's SHA-256 asset digest, rejects unsafe paths/links and checks platform files and VERSION. It stages everything without touching the running installation. Progress and cancellation are available.

Once ready, choose **Install and restart** and confirm the interruption. A copy of the currently running updater operates outside the installation being replaced. It stops the managed server, Webhost, Supervisor and control terminal, saves the existing artifacts/Start/configuration, swaps the staged artifacts and Start, then opens a new control terminal. Server startup follows the auto-start setting. Configuration, data-protection keys, users, downloaded game files and bundles are retained. No unattended update is installed.

Settings-write permission is required to view/check updates. Download/install additionally requires user-management permission. In-place installation is enabled only for a native package with an open Start control terminal. Docker and systemd deployments expose the version check, but are upgraded through Compose/systemd deployment tools; the dashboard does not replace an immutable container or gain host service-manager access.

On an SSH host the new control terminal is a tmux session; use `tmux list-sessions` and attach to `replay-panel-<instance-prefix>`.

The new panel must answer the version and web readiness probes. Failed startup restores the old package automatically. Browser disconnection is expected during installation; the page reconnects and reads the persisted result. If it cannot reconnect, open Start and inspect:

- `.local/panel-update.json`: last job/result.
- `.local/updates/<job>/apply.log`: installation/recovery log.
- `.local/updates/<job>/backup`: previous artifacts, Start, VERSION and configuration.

Keep backups until the new version works. For manual recovery, stop all remaining panel processes using the direct stop command, move the failed artifacts/Start aside, restore the backup artifacts/Start and, only if configuration migration failed, restore panel.json. Keep the keys directory. Start again. If the stop command fails, inspect process identity before touching active binaries. Never replace the game-server directory with a panel package.

When building a release, VERSION must match the assemblies' Directory.Build.props version. The CI packages this file and verifies replacement plus startup-failure rollback on Windows and Linux. Version 0.5.1 uses the former repository URL and cannot verify renamed release assets. Install 1.0.0 manually once, retaining .local; subsequent native releases can be installed in the dashboard.

## Deutsch

Unter **Einstellungen → Panel-Updates** prüft das Panel das neueste stabile öffentliche Release von LandminenTester/blackice-server-panel. Installierte/neue Version und Releasehinweise werden angezeigt. Prüfungen werden eine Minute zwischengespeichert; ein GitHub-Token ist nicht erforderlich. Entwürfe und Vorabversionen werden ausgeschlossen.

**Herunterladen und prüfen** lädt das passende Windows-x64-ZIP beziehungsweise Linux-x64-tar.gz. Der Supervisor prüft Paketlänge und GitHubs SHA-256-Wert, verwirft unsichere Pfade/Links und prüft Plattformdateien und VERSION. Das laufende Paket bleibt währenddessen erhalten. Fortschritt und Abbruch sind verfügbar.

Nach erfolgreicher Prüfung **Installieren und neu starten** auswählen und die Unterbrechung bestätigen. Eine Kopie des bisherigen Updaters läuft außerhalb der auszutauschenden Installation. Sie beendet den verwalteten Server, Webhost, Supervisor und das Steuerterminal, sichert Artefakte/Start/Konfiguration, tauscht das vorbereitete Paket aus und öffnet ein neues Startterminal. Für den Spielserver gilt die Autostart-Einstellung. Konfiguration, Sitzungsschlüssel, Benutzer, Spieldateien und Bundles bleiben erhalten. Updates werden nicht unbeaufsichtigt installiert.

Zum Prüfen wird **Einstellungen ändern** benötigt; Herunterladen/Installieren benötigt zusätzlich **Benutzer verwalten**. Direkte Installation ist nur für native Pakete mit geöffnetem Startterminal aktiviert. Docker- und systemd-Betrieb zeigen den Versionscheck, werden aber mit Compose beziehungsweise den Dienstwerkzeugen aktualisiert. Das Dashboard ersetzt keinen unveränderlichen Container und erhält keinen Zugriff auf den Host-Dienstmanager.

Per SSH läuft das neue Steuerterminal als tmux-Sitzung; mit `tmux list-sessions` anzeigen und an `replay-panel-<Instanzpräfix>` anhängen.

Die neue Version muss Versions- und Webbereitschaftsprüfungen bestehen. Scheitert ihr Start, wird das alte Paket automatisch wiederhergestellt. Währenddessen ist die Browserverbindung kurz unterbrochen; die Seite verbindet sich erneut und liest das gespeicherte Ergebnis. Bei Problemen Start öffnen und folgende Dateien prüfen:

- `.local/panel-update.json`: letzter Auftrag und Ergebnis.
- `.local/updates/<Auftrag>/apply.log`: Installations-/Wiederherstellungslog.
- `.local/updates/<Auftrag>/backup`: bisherige Artefakte, Start, VERSION und Konfiguration.

Sicherungen behalten, bis die neue Version funktioniert. Zur manuellen Wiederherstellung verbliebene Panelprozesse mit dem direkten Stopbefehl beenden, fehlgeschlagene Artefakte/Start zur Seite verschieben und die Sicherung zurückkopieren. panel.json nur bei gescheiterter Konfigurationsmigration zurückspielen; das Schlüsselverzeichnis behalten. Anschließend Start öffnen. Falls der Stopbefehl scheitert, vor dem Dateiaustausch Prozessidentitäten prüfen. Keine Paneldateien in den Spielserverordner kopieren.

VERSION muss beim Releasebau der Assemblyversion aus Directory.Build.props entsprechen. Die CI liefert diese Datei mit und prüft Austausch sowie Wiederherstellung unter Windows und Linux. Version 0.5.1 verwendet den alten Repositorynamen und kann umbenannte Release-Artefakte nicht prüfen. 1.0.0 einmal manuell installieren und .local erhalten; spätere native Releases lassen sich über das Dashboard installieren.
