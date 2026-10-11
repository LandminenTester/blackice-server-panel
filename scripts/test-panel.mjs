// Runs only the disposable ConsoleFixture. Never targets an existing game server.
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { spawn } from 'node:child_process';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { pbkdf2Sync, randomBytes } from 'node:crypto';
import assert from 'node:assert/strict';
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const folder = resolve(root, '.local/panel-test');
await mkdir(folder, { recursive: true });
const password = 'disposable-fixture-password';
const salt = randomBytes(16);
const configPath = resolve(folder, 'panel.json');
const config = { serverRoot: resolve(root, '.local/fixture'), pipeName: 'replay-panel-test-' + process.pid, shutdownMode:'keep-running', stopTimeoutSeconds:10, autoRestart:false, url:'http://127.0.0.1:20774', passwordSalt:salt.toString('base64'), passwordHash:pbkdf2Sync(password,salt,210000,32,'sha256').toString('base64') };
await writeFile(configPath, JSON.stringify(config));
let count = 0;
const supervisor = spawn(resolve(root, 'artifacts/supervisor/Supervisor.exe'), [configPath], { cwd:folder, windowsHide:true, stdio:'ignore' });
let web;
const cookies = new Map(); let csrf;
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
async function request(route, options={}) {
  const response = await fetch(config.url+route, {...options, headers:{ Cookie:[...cookies].map(([k,v])=>k+'='+v).join('; '), ...options.headers }});
  for(const cookie of response.headers.getSetCookie()) { const entry=cookie.split(';')[0], i=entry.indexOf('='); cookies.set(entry.slice(0,i),entry.slice(i+1)); }
  return response;
}
async function post(route,body={}) { return request(route,{method:'POST',headers:{'Content-Type':'application/json','X-CSRF':csrf},body:JSON.stringify(body)}); }
async function startWeb() {
  web=spawn(resolve(root,'artifacts/web/Web.exe'), [], { cwd:resolve(root,'artifacts/web'), env:{...process.env,REPLAY_PANEL_CONFIG:configPath},windowsHide:true,stdio:'ignore' });
  for(let i=0;i<60;i++){try{const r=await request('/api/session');if(r.ok){csrf=(await r.json()).csrf;return;}}catch{}await sleep(250);}throw Error('Test webhost did not start.');
}
async function pipe(action, webControl=false) {
  const payload = (webControl ? 'shutdown' : JSON.stringify({action})).replaceAll("'", "''");
  const script = '$p = New-Object IO.Pipes.NamedPipeClientStream(".","'+config.pipeName+(webControl?'.web':'')+'",[IO.Pipes.PipeDirection]::InOut); $p.Connect(3000); $w=New-Object IO.StreamWriter($p); $r=New-Object IO.StreamReader($p); $w.AutoFlush=$true; $w.WriteLine(\''+payload+'\'); $r.ReadLine() | Out-Null; $p.Dispose()';
  const child=spawn('powershell.exe',['-NoProfile','-Command',script],{windowsHide:true,stdio:'ignore'});
  await new Promise((resolve,reject)=>child.on('exit',code=>code===0?resolve():reject(Error('Pipe shutdown failed.'))));
}
function check(value,name){assert(value,name);count++;console.log('PASS '+name);}
try {
  await startWeb();
  check((await request('/api/status')).status===401,'anonymous status denied');
  check((await request('/api/login',{method:'POST',headers:{'Content-Type':'application/json'},body:'{}'})).status===400,'missing CSRF rejected');
  check((await post('/api/login',{password:'wrong'})).status===401,'wrong password rejected');
  check((await post('/api/login',{password})).status===200,'login accepted');
  csrf=(await(await request('/api/session')).json()).csrf;
  check((await post('/api/server/start')).status===200,'fixture started through web API');
  check((await post('/api/server/start')).status===409,'duplicate start rejected through web API');
  check((await post('/api/command',{text:'echo panel-http-verified'})).status===200,'console command accepted');
  check((await post('/api/command',{text:'echo ok;exit'})).status===409,'command chaining denied');
  await pipe('ignored',true); await sleep(1500); await startWeb();
  check((await(await request('/api/status')).json()).status.state==='running','keep-running survives webhost restart');
  check((await post('/api/settings',{shutdownMode:'stop-server'})).status===200,'stop-server mode persisted');
  await pipe('ignored',true); await sleep(1500); await startWeb();
  check((await(await request('/api/status')).json()).status.state==='stopped','stop-server stops fixture on webhost shutdown');
  check((await post('/api/server/start')).status===200,'fixture can restart');
  check((await post('/api/server/force-stop')).status===200,'explicit forced stop works');
  if(process.argv.includes('--review')) { console.log('Review fixture ready on http://127.0.0.1:20774. Test-only password: disposable-fixture-password'); await new Promise(resolve=>process.once('SIGINT',resolve)); }
  console.log(count+' panel integration checks passed.');
} finally {
  try { await post('/api/server/stop'); } catch {}
  try { await pipe('ignored',true); } catch {}
  try { await pipe('shutdown'); } catch {}
  web?.kill(); supervisor.kill();
}
