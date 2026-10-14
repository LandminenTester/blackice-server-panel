# Accounts and permissions / Benutzer und Berechtigungen

## English

Open **Users** with an account that has **Manage users**. Create an account, choose individual permissions and save. New passwords need 8–256 characters; usernames need 3–32 letters/digits/dots/underscores/hyphens and begin with a letter or digit. Edit existing users to change permissions, disable access or set a new password; leave the password empty to retain it. Delete requires confirmation. At least one enabled user must retain Manage users.

The original administrator keeps all permissions. Legacy configurations are retained; the account list is initialized from the original salted hash when first modified. Upgrading from older releases requires signing in again. Changes to an account revoke its existing cookies and open event streams. API endpoints check permissions for every request, including direct HTTP requests. Password hashes are never sent to the browser. Back up the private panel configuration and key directory.

| Permission | Capability |
|---|---|
| Server start / stop / restart / force-stop | Each server action separately |
| Console read / write | View live output or send game commands |
| Logs read | View the server log file |
| Bundles read | List installed/loaded bundles |
| Bundles start / stop / restart | Each action, also requires Bundles read |
| Bundle upload (`bundles.upload`) | Validate and install compiled bundle ZIPs |
| Bundle build (`bundles.build`) | Manage source projects and execute trusted builds |
| Bundle settings (`bundles.settings`) | Change experimental feature/build preferences |
| Build tools (`bundles.tools`) | Check, install and remove panel-managed build tools |
| Git accounts manage (`bundles.accounts.manage`) | Add, replace, test and remove server-wide provider credentials |
| Git accounts use (`bundles.accounts.use`) | Use shared credentials for authorized repository operations |
| Git writes (`bundles.git.write`) | Create/push remote repositories and use the offered write workflows |
| Settings write | Change general startup, shutdown and other panel settings |
| Users manage | Create, edit, disable and delete accounts; granting other permissions |

Remote creation and delivery require **Bundle build**, **Git accounts use** and **Git writes** together. Per-project workflow overrides require **Bundle settings**. Build-tool jobs require **Build tools**, upload jobs require **Bundle upload**, and source/build job logs require **Bundle build**.

Git accounts are shared by the server installation. Their tokens are never returned by the API; knowing an account label does not grant its use or management. Build and Git-write permissions are powerful developer capabilities, not a safe way to run untrusted code. See [the Bundle Tools security boundary](../SECURITY.md#bundle-tools-experimental).

**Console write is powerful:** raw BlackICE commands can affect bundles and other game state. It is intended for trusted operators. Bundle-only operators should receive bundle permissions without console write. Every signed-in user can view process status and choose a UI language. These panel permissions do not constrain the local OS account or the native server console.

## Deutsch

Unter **Benutzer** kann ein Zugang mit **Benutzer verwalten** weitere Zugänge anlegen. Einzelne Rechte auswählen und speichern. Neue Passwörter benötigen 8–256 Zeichen; Benutzernamen 3–32 Zeichen aus Buchstaben, Zahlen, Punkt, Unterstrich oder Bindestrich und beginnen mit Buchstabe oder Zahl. Beim Bearbeiten lassen sich Rechte ändern, Zugänge deaktivieren und Passwörter neu setzen. Ein leeres Passwortfeld behält das bisherige Passwort bei. Löschen benötigt eine Bestätigung. Mindestens ein aktiver Zugang muss Benutzer verwalten dürfen.

Der ursprüngliche Administrator behält alle Rechte. Die bisherige Konfiguration bleibt erhalten; beim ersten Bearbeiten wird die Benutzerliste aus dem vorhandenen gesalzenen Hash aufgebaut. Nach einem Update von älteren Versionen neu anmelden. Änderungen beenden bestehende Sitzungen und Eventstreams des betroffenen Zugangs. Die API prüft Rechte bei jeder Anfrage, auch bei direkten HTTP-Aufrufen. Passwort-Hashes gelangen nicht in den Browser. Private Konfiguration und Schlüsselverzeichnis sichern.

Server starten, stoppen, neustarten und erzwungen stoppen sind getrennte Rechte. Gleiches gilt für Konsole lesen/schreiben, Logs lesen, Bundles sehen sowie Bundles starten/stoppen/neustarten. Bundle-Aktionen benötigen zusätzlich Bundles sehen. Einstellungen ändern erlaubt Änderungen an Start-/Abschaltverhalten. Benutzer verwalten umfasst Anlegen, Bearbeiten, Deaktivieren, Löschen und die Vergabe weiterer Rechte.

Remote-Erstellung und Auslieferung benötigen `bundles.build`, `bundles.accounts.use` und `bundles.git.write` gemeinsam; Projektausnahmen für den Ablauf `bundles.settings`. Werkzeug-, Upload- und Buildlogs folgen den Rechten der jeweiligen Aktionsart.

Für experimentelle Bundle-Tools gibt es zusätzliche Rechte: `bundles.upload` (ZIP installieren), `bundles.build` (Quellen/Builds), `bundles.settings` (Bundle-Einstellungen), `bundles.tools` (Build-Werkzeuge), `bundles.accounts.manage` (gemeinsame Git-Konten verwalten), `bundles.accounts.use` (deren Zugangsdaten verwenden) und `bundles.git.write` (Remote-Schreibaktionen). Git-Konten gelten für die Serverinstallation; Tokens werden nie an den Browser zurückgegeben. Buildrechte erlauben Codeausführung und gehören nur an vertrauenswürdige Entwickler. Siehe [Sicherheitsgrenze](../SECURITY.md#bundle-tools-experimental).

**Konsole schreiben ist weitreichend:** BlackICE-Befehle können Bundles und weitere Spielzustände ändern. Dieses Recht nur vertrauenswürdigen Administratoren geben. Reine Bundle-Verwalter erhalten Bundle-Rechte ohne Konsolenschreibzugriff. Alle angemeldeten Benutzer sehen den Prozessstatus und dürfen ihre Oberflächensprache wählen. Die Panel-Rechte beschränken weder das lokale Betriebssystemkonto noch die native Serverkonsole.

**Super admin / Super-Admin:** in the user editor, *Super admin* ticks every permission at once and locks the individual switches, so one switch grants full access. Das Häkchen *Super-Admin* wählt im Benutzer-Editor alle Berechtigungen auf einmal aus und sperrt die einzelnen Schalter.

## Bundle workflow permissions

The editor groups capabilities under Server, Console/logs, Bundle operation, Projects/Git, Tools/accounts and Administration.

| Permission | Action |
| --- | --- |
| `bundles.review` | Create provider review requests |
| `bundles.merge` | Explicit direct remote merge |
| `bundles.packages` | Propose isolated package changes |
| `bundles.autostart` | Change managed startup entries |
| `bundles.tags` | Edit server-wide categories |
| `bundles.delete` | Delete bundle runtime files and selected backups |
| `bundles.sources.delete` | Delete local project sources |
| `bundles.data.delete` | Delete verified exclusive local bundle data |

Git actions additionally require `bundles.build`, `bundles.git.write` and `bundles.accounts.use`. Package proposals also require `bundles.review`. Deletion needs bundle-read and bundle-stop permissions, with additional source/data permissions for expanded scope. Existing ordinary Git writers do not automatically receive merge or deletion rights. Full administrators retain all permissions.

These rights govern HTTP operations and job output. Console writing can still issue arbitrary game-server commands, and build permission executes project code as the OS account; neither is a sandbox boundary.
