# Server configuration / Serverkonfiguration

## English

**Settings → Server config** edits the main keys of `cfg/server.cfg`: `sv_name`, `sv_desc`, `sv_region` (ISO 3166-1 alpha-3 country code, chosen from a list), `sv_dlc`, `sv_bindaddr`, `sv_endpoint` and `sv_lan`. Comments and all other lines stay unchanged and the original line endings are kept. The first save creates `cfg/server.cfg.panel.bak`. Changes apply after a server restart. Requires the `settings.write` permission. Names and descriptions must not contain quotes or backslashes.

## Deutsch

**Einstellungen → Serverkonfiguration** bearbeitet die wichtigsten Schlüssel der `cfg/server.cfg`: `sv_name`, `sv_desc`, `sv_region` (ISO-3166-1-alpha-3-Ländercode, per Liste), `sv_dlc`, `sv_bindaddr`, `sv_endpoint` und `sv_lan`. Kommentare und alle anderen Zeilen bleiben unverändert, ebenso die Zeilenenden. Beim ersten Speichern entsteht `cfg/server.cfg.panel.bak`. Änderungen gelten nach einem Serverneustart. Erfordert die Berechtigung `settings.write`. Name und Beschreibung dürfen keine Anführungszeichen oder Backslashes enthalten.

## Additional keys and log archive / Weitere Schlüssel und Log-Archiv

English: The editor also manages `sv_voip_addr`, `sv_privacy`, `sv_ugc_automatic_restarts` and, in `settings/server.settings.json`, `channel`, `auto_update` and `restart_after_update` (containers override these through the `CHANNEL`, `AUTO_UPDATE` and `RESTART_AFTER_UPDATE` environment variables). Region is a typable list of ISO 3166-1 alpha-3 codes. **Console → Log archive** lists the server's `logs/` files grouped by day and server start, shows a file and downloads all logs as one ZIP (permission `logs.read`).

Deutsch: Der Editor verwaltet zusätzlich `sv_voip_addr`, `sv_privacy`, `sv_ugc_automatic_restarts` sowie in `settings/server.settings.json` `channel`, `auto_update` und `restart_after_update` (Container überschreiben sie über die Umgebungsvariablen `CHANNEL`, `AUTO_UPDATE` und `RESTART_AFTER_UPDATE`). Die Region ist eine tippbare Liste mit ISO-3166-1-alpha-3-Codes. **Konsole → Log-Archiv** listet die Dateien aus `logs/` nach Tag und Serverstart, zeigt eine Datei an und lädt alle Logs als ZIP herunter (Berechtigung `logs.read`).

## Bundle details, full start log and update checks / Bundle-Details, Start-Log und Update-Prüfung

English: The bundle list shows `author`, `version` and `description` from the optional `[General]` table of each bundle's `config.toml`. **Console → Download full log (this start)** saves the complete server log file of the current (or last) server start; the log archive lists older starts. **Settings → General → Check for updates automatically** controls whether the panel contacts GitHub and artifacts.replay.re on its own; when off, checks happen only when you click a check button.

Deutsch: Die Bundle-Liste zeigt `author`, `version` und `description` aus der optionalen Tabelle `[General]` der `config.toml` jedes Bundles. **Konsole → Komplettes Log herunterladen (dieser Start)** speichert die vollständige Server-Logdatei des aktuellen (oder letzten) Serverstarts; das Log-Archiv listet ältere Starts. **Einstellungen → Allgemein → Automatisch nach Updates suchen** legt fest, ob das Panel GitHub und artifacts.replay.re selbstständig kontaktiert; ist es aus, wird nur beim Klick auf eine Prüfen-Schaltfläche geprüft.
