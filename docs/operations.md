# Operations / Betrieb

## English

Private configuration: `.local/panel.json`, or the service's configured state path. `REPLAY_PANEL_CONFIG` chooses the file. `REPLAY_PANEL_RUNTIME` chooses Linux IPC storage. Passwords use PBKDF2-SHA256, 210000 iterations, 16-byte random salts. Data-protection keys preserve sessions across Webhost restarts. Protect and back up config and keys together.

**Backup:** gracefully stop the game; copy server `cfg`, `data/ugc` (including bundle VFS), `settings` and any other server-specific persistent data, plus panel state. Store backups privately because registration keys and password hashes are included. **Restore:** stop both panel processes, restore to writable directories, preserve the instance/container identity for Docker, then start Supervisor and Webhost. Test restored data before reopening public game ports.

**Manual updates:** back up first, stop game and panel processes, extract a new panel release outside live files, replace binaries/UI/scripts/docs, keep private state. Update BlackICE separately, preserving cfg, bundles/VFS, logs and settings. Fresh installations disable auto-update. Do not overwrite data with a clean server ZIP. On Docker update the pinned official image separately and recreate only the owned server container while preserving storage and updating the configured full container ID/ownership label. REServer runtime updates are available under Settings → REServer updates (server-updates.md); native panel updates use Settings → Panel updates.

**HTTPS:** run the panel on localhost behind a proxy. Keep the game/voice ports separate. Set explicit `allowedHosts` (including external port if nonstandard), `trustedProxies` (actual proxy IP), and `publicUrl` to the HTTPS URL. Secure cookies are then required. Do not trust arbitrary forwarding headers or expose HTTP directly.

Example private configuration additions:
```json
{"allowedHosts":["panel.example.org"],"trustedProxies":["127.0.0.1"],"publicUrl":"https://panel.example.org"}
```

Example nginx location (within a TLS-enabled virtual host):
```nginx
location / {
 proxy_pass http://127.0.0.1:20772;
 proxy_set_header Host $host;
 proxy_set_header X-Forwarded-Proto https;
 proxy_set_header X-Forwarded-For $remote_addr;
 proxy_buffering off;
 proxy_read_timeout 1h;
}
```

Diagnostics:
- Supervisor unavailable: check same user, matching pipe/socket and config, directory permissions, Supervisor log/service.
- External server: stop it in the original console before starting through the panel.
- Unknown bundle state: refresh and inspect native logs; a timed-out action is not automatically retried.
- Installation error: inspect Supervisor error log. Check network/registry, empty target, available disk and Linux dependencies.
- Graceful stop timed out: inspect game state before explicitly forcing it.
- Docker unavailable: check socket group, daemon health and owned container ID/label.
- Wrong login after migration: use `admin` with the existing password.
- Lost password: stop both processes, privately back up state and replace passwordSalt/passwordHash using the same documented PBKDF2 parameters with a fresh salt. Never set a plaintext password or publish credentials.

## Deutsch

Private Konfiguration: `.local/panel.json` oder der Zustandsordner des Diensts. `REPLAY_PANEL_CONFIG` bestimmt die Datei, `REPLAY_PANEL_RUNTIME` den Linux-IPC-Pfad. Passwörter verwenden PBKDF2-SHA256 mit 210000 Iterationen und zufälligem 16-Byte-Salt. Konfiguration und Data-Protection-Schlüssel gemeinsam privat sichern.

**Sicherung:** Spielserver geordnet stoppen. `cfg`, `data/ugc` einschließlich VFS, `settings` und weitere serverbezogene Daten sowie privaten Panelzustand kopieren. Sicherungen enthalten Schlüssel und Hashes. **Wiederherstellung:** Beide Panelprozesse beenden, Daten in beschreibbare Ordner zurückspielen, Docker-Instanz-/Containerzugehörigkeit erhalten, Dienste starten und Daten vor öffentlicher Freigabe prüfen.

**Manuelle Updates:** Vorher sichern, Spielserver und Panelprozesse stoppen. Neues Panelpaket getrennt entpacken, Programme/UI/Skripte/Dokumentation ersetzen und privaten Zustand erhalten. BlackICE separat aktualisieren; cfg, Bundles/VFS, Logs und Einstellungen erhalten. Automatische Updates sind bei neuen Installationen deaktiviert. Unter Docker Imageversion kontrolliert wechseln, nur den zugehörigen Servercontainer mit dauerhaftem Speicher neu erstellen und vollständige Container-ID sowie Zugehörigkeitslabel in der privaten Konfiguration aktualisieren. REServer-Laufzeitupdates stehen unter Einstellungen → REServer-Updates bereit (server-updates.md); native Panel-Updates unter Einstellungen → Panel-Updates.

**HTTPS:** Panel auf localhost hinter einem TLS-Proxy betreiben. `allowedHosts` mit externem Host, `trustedProxies` mit tatsächlicher Proxy-IP und `publicUrl` mit HTTPS-Adresse setzen. Dann sind sichere Cookies verpflichtend. Weiterleitungsheader fremder Clients nicht vertrauen und HTTP nicht öffentlich freigeben. Oben stehen Konfigurations- und nginx-Beispiele; SSE benötigt deaktiviertes Proxybuffering und ausreichend Timeout.

**Diagnose:** Bei fehlendem Supervisor Benutzer, Pipe/Socket, Konfiguration, Rechte und Dienstlog prüfen. Externen Server in seiner bisherigen Konsole beenden. Unbekannte Bundlezustände aktualisieren und originale Logs lesen; Zeitüberschreitungen werden nicht blind wiederholt. Installationsfehler im Supervisor-Fehlerlog prüfen: Netzwerk, Registry, Speicherplatz, leerer Ordner und Linux-Bibliotheken. Bei Docker Socketgruppe und Containerzugehörigkeit prüfen. Nach Migration mit `admin` und dem bisherigen Passwort anmelden. Verlorenes Passwort nach Stop und privater Sicherung durch neuen Salt/Hash mit den obigen Parametern ersetzen; niemals Klartext speichern.
