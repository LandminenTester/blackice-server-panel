# Accounts and permissions / Benutzer und Berechtigungen

## English

Open **Users** with an account that has **Manage users**. Create an account, choose individual permissions and save. New passwords need 12–256 characters; usernames need 3–32 letters/digits/dots/underscores/hyphens and begin with a letter or digit. Edit existing users to change permissions, disable access or set a new password; leave the password empty to retain it. Delete requires confirmation. At least one enabled user must retain Manage users.

The original administrator keeps all permissions. Legacy configurations are retained; the account list is initialized from the original salted hash when first modified. Upgrading from older releases requires signing in again. Changes to an account revoke its existing cookies and open event streams. API endpoints check permissions for every request, including direct HTTP requests. Password hashes are never sent to the browser. Back up the private panel configuration and key directory.

| Permission | Capability |
|---|---|
| Server start / stop / restart / force-stop | Each server action separately |
| Console read / write | View live output or send game commands |
| Logs read | View the server log file |
| Bundles read | List installed/loaded bundles |
| Bundles start / stop / restart | Each action, also requires Bundles read |
| Settings write | Change startup and shutdown behavior |
| Users manage | Create, edit, disable and delete accounts; granting other permissions |

**Console write is powerful:** raw BlackICE commands can affect bundles and other game state. It is intended for trusted operators. Bundle-only operators should receive bundle permissions without console write. Every signed-in user can view process status and choose a UI language. These panel permissions do not constrain the local OS account or the native server console.

## Deutsch

Unter **Benutzer** kann ein Zugang mit **Benutzer verwalten** weitere Zugänge anlegen. Einzelne Rechte auswählen und speichern. Neue Passwörter benötigen 12–256 Zeichen; Benutzernamen 3–32 Zeichen aus Buchstaben, Zahlen, Punkt, Unterstrich oder Bindestrich und beginnen mit Buchstabe oder Zahl. Beim Bearbeiten lassen sich Rechte ändern, Zugänge deaktivieren und Passwörter neu setzen. Ein leeres Passwortfeld behält das bisherige Passwort bei. Löschen benötigt eine Bestätigung. Mindestens ein aktiver Zugang muss Benutzer verwalten dürfen.

Der ursprüngliche Administrator behält alle Rechte. Die bisherige Konfiguration bleibt erhalten; beim ersten Bearbeiten wird die Benutzerliste aus dem vorhandenen gesalzenen Hash aufgebaut. Nach einem Update von älteren Versionen neu anmelden. Änderungen beenden bestehende Sitzungen und Eventstreams des betroffenen Zugangs. Die API prüft Rechte bei jeder Anfrage, auch bei direkten HTTP-Aufrufen. Passwort-Hashes gelangen nicht in den Browser. Private Konfiguration und Schlüsselverzeichnis sichern.

Server starten, stoppen, neustarten und erzwungen stoppen sind getrennte Rechte. Gleiches gilt für Konsole lesen/schreiben, Logs lesen, Bundles sehen sowie Bundles starten/stoppen/neustarten. Bundle-Aktionen benötigen zusätzlich Bundles sehen. Einstellungen ändern erlaubt Änderungen an Start-/Abschaltverhalten. Benutzer verwalten umfasst Anlegen, Bearbeiten, Deaktivieren, Löschen und die Vergabe weiterer Rechte.

**Konsole schreiben ist weitreichend:** BlackICE-Befehle können Bundles und weitere Spielzustände ändern. Dieses Recht nur vertrauenswürdigen Administratoren geben. Reine Bundle-Verwalter erhalten Bundle-Rechte ohne Konsolenschreibzugriff. Alle angemeldeten Benutzer sehen den Prozessstatus und dürfen ihre Oberflächensprache wählen. Die Panel-Rechte beschränken weder das lokale Betriebssystemkonto noch die native Serverkonsole.
