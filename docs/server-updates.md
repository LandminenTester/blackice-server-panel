# REServer updates / REServer-Updates

## English

Settings → REServer updates checks the official alpha build. Prepare downloads managed files from artifacts.replay.re/server_windows/alpha or server_linux/alpha and verifies every SHA-256 from contents.json. Applying stops an owned running server gracefully, backs up runtime files, replaces them and restarts only a previously running server. A startup failure restores previous runtime files. The panel stays available during the operation.

There is currently only the alpha channel in the [official update guide](https://docs.replay.re/servers/updates/). No public historical commit-download catalog was verified. The target selector therefore offers official current alpha plus earlier builds saved by this panel. A previous build with unknown identity remains a manual recovery backup, not a guessed version. Windows ZIP seed may be older than alpha: the verified panel update adds the bridge modules required by current REServer.

cfg/server.cfg, settings, bundles, VFS and logs are not managed update targets. Native and Docker use the same platform manifest; Docker server files are in its persistent mounted directory, rather than being lost on container recreation. Keep the runtime libraries required by the official Linux image.

Check/status requires settings.write. Preparing/installing additionally requires users.manage; all mutations require CSRF. Writes to server/bundles are blocked during installation. A failed or interrupted operation requires inspection before retry; a recovery error requires manual restoration. Downloads can be cancelled before installation. Prepared jobs are not resumed automatically across Supervisor restarts.

Backups and jobs are stored in private state/server-updates/<job-id>. Retain enough disk space for staging and backup (up to several GB). Backups are not purged automatically because they provide selectable historical builds. Stop the stack before manual recovery: copy the saved backup runtime files back to the configured server directory, restore metadata, preserve all private game data, and restart. Do not replace the entire game directory with a clean download.

GET /api/server-updates returns current/latest commits, known selectable versions and job status. POST /api/server-updates/check, download, cancel, apply manages the operation; download accepts target. Unknown/unpublished versions are rejected. Run dotnet run --project tests/ServerUpdates.Tests for integrity, cancellation, target selection and startup rollback coverage.

## Deutsch

Unter Einstellungen → REServer-Updates die offizielle Alpha-Version prüfen, Dateien vorbereiten und anschließend mit Bestätigung installieren. SHA-256-Prüfung, geordneter Stop, Sicherung und Wiederherstellung bei Startfehlern sind enthalten. Ein zuvor laufender Spielserver startet wieder; ein gestoppter bleibt gestoppt. Das Panel bleibt erreichbar.

Gezielte Auswahl: aktuelles offizielles Alpha und frühere vom Panel gesicherte Builds. Beliebige historische Commits werden nicht als herunterladbar dargestellt, da dafür kein offizieller Katalog bestätigt wurde. Ein alter Windows-ZIP-Seed kann zunächst aktualisiert werden, um die Bridge-Module zu erhalten.

Spielkonfiguration, Einstellungen, Bundles/VFS und Logs bleiben erhalten. Das gilt auch für Dateien im dauerhaften Docker-Servermount. Prüfen benötigt settings.write; Vorbereitung und Installation zusätzlich users.manage. Schreibaktionen werden während der Installation gesperrt. Vor dem Installieren kann der Download abgebrochen werden.

Sicherungen unter privatem state/server-updates bleiben für gezielte Wiederherstellung erhalten; genügend Speicher für Stage und Backup vorsehen. Unterbrochene Vorgänge prüfen. Bei fehlgeschlagener Wiederherstellung Stack stoppen und verwaltete Laufzeitdateien aus dem Backup zurückkopieren; keine privaten Spielverzeichnisse überschreiben.
