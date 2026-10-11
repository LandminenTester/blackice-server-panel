import { readFile, writeFile, mkdir, access } from 'node:fs/promises';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { randomBytes, pbkdf2Sync, createHash } from 'node:crypto';
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const file = resolve(root, '.local/panel.json');
await mkdir(resolve(root, '.local'), { recursive: true });
try { await access(file); console.log('Existing panel configuration preserved.'); }
catch {
  const serverRoot = resolve(process.argv[2] ?? '../ReplayRE Server');
  await access(resolve(serverRoot, 'REServer.exe'));
  const password = randomBytes(18).toString('base64url');
  const salt = randomBytes(16);
  const config = { serverRoot, pipeName: 'replay-panel-' + createHash('sha256').update(serverRoot.toLowerCase()).digest('hex').slice(0,16), shutdownMode: 'keep-running', stopTimeoutSeconds: 30, autoRestart: false, url: process.argv[3] ?? 'http://127.0.0.1:20772', passwordSalt: salt.toString('base64'), passwordHash: pbkdf2Sync(password, salt, 210000, 32, 'sha256').toString('base64') };
  await writeFile(file, JSON.stringify(config, null, 2));
  await writeFile(resolve(root, '.local/login.txt'), 'Local panel: ' + config.url + '\nPassword: ' + password + '\nKeep this file private. It is excluded from Git.\n');
  console.log('Panel configured. Login details are in .local/login.txt (not printed).');
}
