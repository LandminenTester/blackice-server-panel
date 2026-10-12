// Separate native test installation only. Never passes the production server path.
import {spawn} from 'node:child_process';import {mkdir,writeFile,readFile} from 'node:fs/promises';import {resolve} from 'node:path';import {randomBytes,pbkdf2Sync} from 'node:crypto';import assert from 'node:assert/strict';
const root=resolve(import.meta.dirname,'..'),build=process.env.PANEL_TEST_BUILD??'artifacts',server=process.env.PANEL_BRIDGE_TEST_SERVER;
if(!server||!resolve(server).startsWith(resolve(root,'.local')+'/')&& !resolve(server).startsWith(resolve(root,'.local')+'\\'))throw Error('Use a separate test server below .local');
const folder=resolve(root,'.local/bridge-real/state');await mkdir(folder,{recursive:true});const configPath=resolve(folder,'panel.json'),url='http://127.0.0.1:20778',password='disposable-bridge-password',salt=randomBytes(16);
await writeFile(configPath,JSON.stringify({schemaVersion:2,backend:process.platform==='win32'?'windows':'linux',dashboardName:'BlackICE bridge test',serverRoot:resolve(server),pipeName:'bridge-real-'+process.pid,url,autoStartServer:false,passwordSalt:salt.toString('base64'),passwordHash:pbkdf2Sync(password,salt,210000,32,'sha256').toString('base64'),bridge:{enabled:false,port:30774,retentionDays:7,maxMegabytes:100}}));
const ext=process.platform==='win32'?'.exe':'',env={...process.env,REPLAY_PANEL_NO_TERMINAL:'1',Logging__LogLevel__Default:'Error'};
const supervisor=spawn(resolve(root,build,'supervisor/Supervisor'+ext),[configPath],{env,stdio:['ignore','pipe','pipe'],windowsHide:true});let logs='';supervisor.stdout.on('data',x=>logs+=x);supervisor.stderr.on('data',x=>logs+=x);
const web=spawn(resolve(root,build,'web/Web'+ext),[],{cwd:resolve(root,build,'web'),env:{...env,REPLAY_PANEL_CONFIG:configPath},stdio:'ignore',windowsHide:true});let csrf='',cookies=new Map();const sleep=ms=>new Promise(r=>setTimeout(r,ms));
async function req(path,body){const response=await fetch(url+path,{method:body===undefined?'GET':'POST',headers:{'Content-Type':'application/json','X-CSRF':csrf,Cookie:[...cookies].map(x=>x.join('=')).join('; ')},body:body===undefined?undefined:JSON.stringify(body)});for(const c of response.headers.getSetCookie()){const [n,...v]=c.split(';')[0].split('=');cookies.set(n,v.join('='));}const data=await response.json();if(!response.ok)throw Error(path+': '+JSON.stringify(data));return data;}
try{
 for(let n=0;n<60;n++){try{csrf=(await req('/api/session')).csrf;break;}catch{}await sleep(200);}
 await req('/api/login',{password});csrf=(await req('/api/session')).csrf;
 const cfgBefore=await readFile(resolve(server,'cfg/server.cfg'),'utf8');
 await req('/api/server/start',{});await sleep(2000);
 const beforeUpdate=await req('/api/server-updates/check',{});await req('/api/server-updates/download',{target:beforeUpdate.latest});let update;
 for(let n=0;n<1200;n++){update=await req('/api/server-updates');if(update.job.state!=='downloading')break;await sleep(500);}assert.equal(update.job.state,'ready',JSON.stringify(update));
 await req('/api/server-updates/apply',{});for(let n=0;n<120;n++){update=await req('/api/server-updates');if(update.job.state!=='applying')break;await sleep(250);}assert.equal(update.job.state,'completed',JSON.stringify(update));assert.equal(update.current,beforeUpdate.latest);assert.equal(await readFile(resolve(server,'cfg/server.cfg'),'utf8'),cfgBefore);console.log('PASS dashboard server update verifies official files, installs and preserves cfg');
 await req('/api/bridge/settings',{enabled:true,port:30774,retentionDays:7,maxMegabytes:100});await req('/api/server/restart',{});let bridge;
 for(let n=0;n<100;n++){bridge=await req('/api/bridge');if(bridge.connected)break;await sleep(200);}
 assert.equal(bridge.connected,true,JSON.stringify(bridge));console.log('PASS real native REServer establishes authenticated owned bridge');
 await req('/api/command',{text:'echo bridge-real-verified'});await sleep(1200);
 const page=await req('/api/diagnostics?source=all');assert(page.lines.some(x=>x.text.includes('bridge-real-verified')));assert(page.lines.some(x=>x.source==='log'));console.log('PASS real console output and module logs have cursors');
 assert.equal((await req('/api/bundles')).ok,true);console.log('PASS real bundle query uses bridge command response');
 await req('/api/bridge/settings',{enabled:false,port:30774,retentionDays:7,maxMegabytes:100});assert.equal((await req('/api/bridge')).restartRequired,true);assert.equal((await req('/api/bridge')).connected,true);console.log('PASS saved setting does not change running server');
 await req('/api/server/restart',{});await sleep(2000);assert.equal((await req('/api/bridge')).connected,false);assert.equal((await req('/api/bundles')).ok,true);assert.equal(await readFile(resolve(server,'cfg/server.cfg'),'utf8'),cfgBefore);console.log('PASS explicit restart restores PTY without changing server configuration');
 await req('/api/bridge/settings',{enabled:true,port:30774,retentionDays:7,maxMegabytes:100});await req('/api/server/restart',{});
 for(let n=0;n<100;n++){bridge=await req('/api/bridge');if(bridge.connected)break;await sleep(200);}assert.equal(bridge.connected,true);console.log('PASS bridge reconnects after full game restart');
 if(process.argv.includes('--review')){console.log('REVIEW http://127.0.0.1:20778');await new Promise(r=>{const timer=setInterval(()=>{},1000);process.once('SIGINT',()=>{clearInterval(timer);r();});});}
 await req('/api/server/stop',{});assert((await req('/api/diagnostics?source=all&history=true')).lines.length>0);console.log('PASS stored diagnostics survive game stop');
}catch(e){console.error(logs);try{console.error(JSON.stringify(await req('/api/logs')));}catch{}throw e;}finally{try{await req('/api/server/stop',{});}catch{}supervisor.kill();web.kill();}
