// Runs only the disposable ConsoleFixture. Never targets an existing game server.
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { spawn } from 'node:child_process';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { pbkdf2Sync, randomBytes } from 'node:crypto';
import assert from 'node:assert/strict';
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const build = process.env.PANEL_TEST_BUILD ?? 'artifacts';
const folder = resolve(root, '.local/panel-test');
await mkdir(folder, { recursive: true });
const suffix=process.platform==='win32'?'.exe':'';
const password = 'disposable-fixture-password';
const salt = randomBytes(16);
const configPath = resolve(folder, 'panel.json');
const config = { schemaVersion:2,autoStartServer:false, backend:process.platform==='win32'?'windows':'linux', serverRoot: resolve(root, '.local/fixture'), pipeName: 'replay-panel-test-' + process.pid, shutdownMode:'keep-running', stopTimeoutSeconds:10, autoRestart:false, url:'http://127.0.0.1:20774', passwordSalt:salt.toString('base64'), passwordHash:pbkdf2Sync(password,salt,210000,32,'sha256').toString('base64') };
await writeFile(configPath, JSON.stringify(config));
for (const name of ['fixture-bundle','parked-bundle','broken-bundle']) {
  const directory = resolve(config.serverRoot,'data/ugc',name);
  await mkdir(directory,{recursive:true}); await writeFile(resolve(directory,'config.toml'),'# Disposable bundle fixture\n');
}
let count = 0;
const supervisor = spawn(resolve(root, build+'/supervisor/Supervisor'+suffix), [configPath], { cwd:folder, windowsHide:true,env:{...process.env,REPLAY_PANEL_NO_TERMINAL:'1'}, stdio:'ignore' });
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
  web=spawn(resolve(root,build+'/web/Web'+suffix), [], { cwd:resolve(root,build+'/web'), env:{...process.env,REPLAY_PANEL_NO_TERMINAL:'1',REPLAY_PANEL_CONFIG:configPath},windowsHide:true,stdio:'ignore' });
  for(let i=0;i<60;i++){try{const r=await request('/api/session');if(r.ok){csrf=(await r.json()).csrf;return;}}catch{}await sleep(250);}throw Error('Test webhost did not start.');
}
async function pipe(action, webControl=false) {
  const net=await import('node:net');const os=await import('node:os');
  const name=config.pipeName+(webControl?'.web':'');
  const endpoint=process.platform==='win32'?'\\\\.\\pipe\\'+name:(process.env.REPLAY_PANEL_RUNTIME??resolve(os.tmpdir(),'replay-panel-'+os.userInfo().username))+'/'+name+'.sock';
  await new Promise((resolve,reject)=>{const client=net.createConnection(endpoint);client.on('connect',()=>client.write((webControl?'shutdown':JSON.stringify({action}))+'\n'));client.on('data',()=>{client.end();resolve();});client.on('error',reject);});

}
function check(value,name){assert(value,name);count++;console.log('PASS '+name);}
try {
  await startWeb();
  check((await request('/api/status')).status===401,'anonymous status denied');
  check((await request('/api/bundles')).status===401,'anonymous bundle list denied');
  check((await request('/api/login',{method:'POST',headers:{'Content-Type':'application/json'},body:'{}'})).status===400,'missing CSRF rejected');
  check((await post('/api/login',{password:'wrong'})).status===401,'wrong password rejected');
  check((await post('/api/login',{password})).status===200,'login accepted');
  csrf=(await(await request('/api/session')).json()).csrf;
  check((await post('/api/server/start')).status===200,'fixture started through web API');
  check((await post('/api/server/start')).status===409,'duplicate start rejected through web API');
  const initialBundles = await (await request('/api/bundles')).json();
  check(initialBundles.ok && initialBundles.canControl && initialBundles.bundles.some(x=>x.name==='fixture-bundle' && x.state==='running') && initialBundles.bundles.some(x=>x.name==='parked-bundle' && x.state==='stopped'),'native loaded list merged with installed stopped bundles');
  check((await request('/api/bundles/parked-bundle/start',{method:'POST',headers:{'Content-Type':'application/json'},body:'{}'})).status===400,'bundle actions require CSRF');
  check((await post('/api/bundles/parked-bundle/start')).status===200,'stopped bundle starts and is confirmed');
  check((await post('/api/bundles/parked-bundle/start')).status===409,'stale bundle start rejected');
  check((await post('/api/bundles/parked-bundle/restart')).status===200,'running bundle restarts');
  const stoppedBundle = await (await post('/api/bundles/parked-bundle/stop')).json();
  check(stoppedBundle.bundles.some(x=>x.name==='parked-bundle' && x.state==='stopped'),'stopped bundle remains available to start');
  check((await post('/api/bundles/does-not-exist/start')).status===409,'unknown bundle rejected');
  check((await post('/api/bundles/parked-bundle%3Bexit/start')).status===409,'bundle command injection rejected');
  check((await post('/api/bundles/parked-bundle/delete')).status===409,'unsupported bundle action rejected');
  check((await post('/api/bundles/broken-bundle/start')).status===409,'native bundle failure is not reported as success');
  const concurrent = await Promise.all([request('/api/bundles'),request('/api/bundles')]);
  check((await Promise.all(concurrent.map(x=>x.json()))).every(x=>x.ok && x.canControl),'concurrent bundle queries stay isolated');
  await post('/api/command',{text:'fixture-malformed-bundles'});
  const malformed = await (await request('/api/bundles')).json();
  check(!malformed.ok && !malformed.canControl,'unrecognized bundle output disables actions');
  check((await (await request('/api/bundles')).json()).ok,'bundle query recovers after malformed output');
  check((await post('/api/command',{text:'echo panel-http-verified'})).status===200,'console command accepted');
  check((await post('/api/command',{text:'echo ok;exit'})).status===409,'command chaining denied');
  const liveEvents = await request('/api/events');
  const eventReader = liveEvents.body.getReader(); await eventReader.read();
  await pipe('ignored',true); await sleep(1500); await startWeb();
  const eventClosed = await Promise.race([
    (async()=>{ while (!(await eventReader.read()).done) {} return true; })(),
    sleep(5000).then(()=>false)
  ]);
  check(eventClosed,'open live event stream closes on webhost shutdown');
  check((await(await request('/api/status')).json()).status.state==='running','keep-running survives webhost restart');
  const recoveredBundles = await (await request('/api/bundles')).json();
  check(recoveredBundles.ok && recoveredBundles.bundles.some(x=>x.name==='fixture-bundle' && x.state==='running') && recoveredBundles.bundles.some(x=>x.name==='parked-bundle' && x.state==='stopped'),'bundle states recovered after webhost restart');
  check((await post('/api/settings',{shutdownMode:'stop-server'})).status===200,'stop-server mode persisted');
  await pipe('ignored',true); await sleep(1500); await startWeb();
  check((await(await request('/api/status')).json()).status.state==='stopped','stop-server stops fixture on webhost shutdown');
  const offlineBundles = await (await request('/api/bundles')).json();
  check(offlineBundles.ok && !offlineBundles.canControl && offlineBundles.bundles.every(x=>x.state==='stopped'),'stopped server lists installed bundles without allowing actions');
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
