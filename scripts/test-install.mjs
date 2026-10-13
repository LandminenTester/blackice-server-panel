import {spawn,execFileSync} from 'node:child_process';
import {mkdir,writeFile,readFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {randomBytes} from 'node:crypto';
import assert from 'node:assert/strict';
const root=resolve(import.meta.dirname,'..'),build=process.env.PANEL_TEST_BUILD??'artifacts';
const folder=resolve(root,'.local/installer-'+randomBytes(5).toString('hex'));
await mkdir(folder,{recursive:true});
const backend=process.argv.includes('--linux')?'linux':process.argv.includes('--docker')?'docker':'windows';
const configPath=resolve(folder,'panel.json'),url='http://127.0.0.1:20776';
let key='',output='',csrf='',cookies=new Map(),supervisor,web,restartWeb,stopWeb;
const target=backend==='windows'?resolve(folder,'Replay Download'):'/server/download';
if(backend==='windows') {
 await writeFile(configPath,JSON.stringify({schemaVersion:2,autoStartServer:false,gamePort:30770,voicePort:30771,pipeName:'panel-install-'+process.pid,url}));
 supervisor=spawn(resolve(root,build,'supervisor/Supervisor.exe'),[configPath],{env:{...process.env,REPLAY_PANEL_NO_TERMINAL:'1'},stdio:['ignore','pipe','inherit']});
 supervisor.stdout.on('data',chunk=>{output+=chunk;key=[...output.matchAll(/Setup key: ([A-Z0-9]+)/g)].pop()?.[1]??'';});
 restartWeb=()=>{web=spawn(resolve(root,build,'web/Web.exe'),[],{cwd:resolve(root,build,'web'),env:{...process.env,REPLAY_PANEL_CONFIG:configPath},stdio:'ignore'});};restartWeb();
 stopWeb=async()=>{const previous=web;execFileSync('node',[resolve(root,'scripts/control.mjs'),'stop-web'],{cwd:root,env:{...process.env,REPLAY_PANEL_CONFIG:configPath},stdio:'ignore'});if(previous.exitCode===null)await new Promise((resolve,reject)=>{const timer=setTimeout(()=>reject(Error('Webhost shutdown timed out')),10000);previous.once('exit',()=>{clearTimeout(timer);resolve();});});};
} else {
 // Containers/volumes use a unique test prefix; no production mounts.
 const name='panel-install-'+process.pid;const volume=name+'-files';
 execFileSync('docker',['volume','create',volume]);
 execFileSync('docker',['run','--rm','--user','0','--entrypoint','sh','-v',volume+':/test','replay-panel:next','-c','mkdir -p /test/state /test/run /test/server && chown -R 10001:10001 /test']);
 {
   const initial={schemaVersion:2,autoStartServer:false,backend,url:'http://0.0.0.0:20772',allowedHosts:['127.0.0.1:20776'],gamePort:30780,voicePort:30781};
   execFileSync('docker',['run','--rm','--user','0','--entrypoint','sh','-v',volume+':/test','replay-panel:next','-c','printf %s "$1" > /test/state/panel.json && chown 10001:10001 /test/state/panel.json','sh',JSON.stringify(initial)]);
 }
 const dockerGid=process.platform==='linux'?execFileSync('stat',['-c','%g','/var/run/docker.sock'],{encoding:'utf8'}).trim():'0';
 const hostPath=execFileSync('docker',['volume','inspect',volume,'--format','{{.Mountpoint}}'],{encoding:'utf8'}).trim()+'/download';
 const env=['-e','REPLAY_PANEL_CONFIG=/test/state/panel.json','-e','REPLAY_PANEL_RUNTIME=/test/run','-e','REPLAY_PANEL_MOUNTS=/server','-e','REPLAY_PANEL_URL=http://0.0.0.0:20772','-e','REPLAY_PANEL_HOSTS=127.0.0.1:20776'];
 execFileSync('docker',['run','-d','--name',name+'-supervisor',...env,'-v',volume+':/test','-v',volume+':/server',...(backend==='docker'?['--user','10001:'+dockerGid,'-v','/var/run/docker.sock:/var/run/docker.sock','-e','REPLAY_PANEL_HOST_SERVER='+hostPath]:[]),'--entrypoint','/app/artifacts/supervisor/Supervisor','replay-panel:next','/test/state/panel.json']);
 execFileSync('docker',['run','-d','--name',name+'-web',...env,'-v',volume+':/test','-p','127.0.0.1:20776:20772','replay-panel:next']);
 stopWeb=()=>execFileSync('docker',['stop',name+'-web'],{stdio:'ignore'});
 restartWeb=()=>execFileSync('docker',['start',name+'-web'],{stdio:'ignore'});
 supervisor={kill(){if(backend==='docker'){try{const data=JSON.parse(execFileSync('docker',['exec',name+'-supervisor','cat','/test/state/panel.json'],{encoding:'utf8'}));if(/^[a-f0-9]{64}$/.test(data.containerId))execFileSync('docker',['rm','-f',data.containerId]);if(/^[a-f0-9]{32}$/.test(data.instanceId)){const ids=execFileSync('docker',['ps','-aq','--filter','label=replay.panel.instance='+data.instanceId],{encoding:'utf8'}).trim().split(/\s+/).filter(x=>/^[a-f0-9]{12,64}$/.test(x));if(ids.length)execFileSync('docker',['rm','-f',...ids]);try{execFileSync('docker',['network','disconnect','-f','blackice-bridge-'+data.instanceId,name+'-supervisor']);execFileSync('docker',['network','rm','blackice-bridge-'+data.instanceId]);}catch{}} }catch{}}execFileSync('docker',['rm','-f',name+'-supervisor']);}};
 web={kill(){execFileSync('docker',['rm','-f',name+'-web']);execFileSync('docker',['volume','rm',volume]);}};
 for(let i=0;i<30&&!key;i++){key=execFileSync('docker',['logs',name+'-supervisor'],{encoding:'utf8'}).match(/Setup key: ([A-Z0-9]+)/)?.[1]??'';await new Promise(r=>setTimeout(r,200));}
}
async function req(path,body){const response=await fetch(url+path,{method:body?'POST':'GET',headers:{'Content-Type':'application/json','X-CSRF':csrf,Cookie:[...cookies].map(x=>x.join('=')).join('; ')},body:body?JSON.stringify(body):undefined});for(const c of response.headers.getSetCookie()){const [n,...v]=c.split(';')[0].split('=');cookies.set(n,v.join('='));}if(!response.ok)throw Error(await response.text());return response.json();}
try{
 for(let i=0;i<50;i++){try{csrf=(await req('/api/session')).csrf;if(key)break;}catch{}await new Promise(r=>setTimeout(r,200));}
 await req('/api/setup/install',{key,serverRoot:target,backend});
 await req('/api/setup/cancel',{key});let job;
 for(let i=0;i<60;i++){job=await req('/api/setup/job',{key});if(job.state!=='running')break;await new Promise(r=>setTimeout(r,200));}
 assert.equal(job.state,'cancelled');console.log('PASS official download cancellation');
 await req('/api/setup/install',{key,serverRoot:target,backend});
 for(let i=0;i<1800;i++){job=await req('/api/setup/job',{key});if(job.state!=='running')break;await new Promise(r=>setTimeout(r,500));}
 assert.equal(job.state,'completed',JSON.stringify(job));console.log('PASS official '+backend+' download, extraction and validation');
 if(backend!=='docker'){const nativeCfg=backend==='windows'?await readFile(resolve(target,'cfg/server.cfg'),'utf8'):execFileSync('docker',['exec','panel-install-'+process.pid+'-supervisor','cat',target+'/cfg/server.cfg'],{encoding:'utf8'});assert(nativeCfg.includes(backend==='windows'?'0.0.0.0:30770':'0.0.0.0:30780'));console.log('PASS native installer respects configured game port');}
 await req('/api/setup/finish',{key,serverRoot:target,backend,dashboardName:'Installer test',username:'test-admin',password:'test-long-password',confirmation:'test-long-password',locale:'en'});
 await req('/api/login',{username:'test-admin',password:'test-long-password'});
 csrf=(await req('/api/session')).csrf;
 await req('/api/server/start',{});console.log('PASS downloaded Replay starts');
 await new Promise(r=>setTimeout(r,2500));await req('/api/command',{text:'echo installer-tested'});
 
 await req('/api/settings',{shutdownMode:'keep-running'});
 await stopWeb();restartWeb();
 for(let i=0;i<50;i++){try{csrf=(await req('/api/session')).csrf;break;}catch{}await new Promise(r=>setTimeout(r,200));}
 assert.equal((await req('/api/status')).status.state,'running');console.log('PASS '+backend+' keep-running survives Webhost restart');
 const list=await req('/api/bundles');if(!list.ok){console.log('Bundle query diagnostic',JSON.stringify(list));const response=await fetch(url+'/api/events',{headers:{Cookie:[...cookies].map(([k,v])=>k+'='+v).join('; ')}});const reader=response.body.getReader();console.log(new TextDecoder().decode((await reader.read()).value));await reader.cancel();}assert.equal(list.ok,true);console.log('PASS '+backend+' bundle query through managed terminal');
 await req('/api/settings',{shutdownMode:'stop-server'});
 await stopWeb();restartWeb();
 for(let i=0;i<50;i++){try{csrf=(await req('/api/session')).csrf;break;}catch{}await new Promise(r=>setTimeout(r,200));}
 assert.equal((await req('/api/status')).status.state,'stopped');console.log('PASS '+backend+' stop-server mode gracefully stops Replay');
 await req('/api/server/start',{});await new Promise(r=>setTimeout(r,1500));
 await req('/api/server/restart',{});await new Promise(r=>setTimeout(r,1500));assert.equal((await req('/api/status')).status.state,'running');console.log('PASS '+backend+' restart');


 const version=await req('/api/server-updates/check',{});await req('/api/server-updates/download',{target:version.latest});let serverJob;
 for(let n=0;n<1800;n++){serverJob=await req('/api/server-updates');if(serverJob.job.state!=='downloading')break;await new Promise(r=>setTimeout(r,500));}assert.equal(serverJob.job.state,'ready',JSON.stringify(serverJob));await req('/api/server-updates/apply',{});
 for(let n=0;n<160;n++){serverJob=await req('/api/server-updates');if(serverJob.job.state!=='applying')break;await new Promise(r=>setTimeout(r,250));}assert.equal(serverJob.job.state,'completed',JSON.stringify(serverJob));console.log('PASS '+backend+' official server update through dashboard API');
 await req('/api/bridge/settings',{enabled:true,port:30774,retentionDays:7,maxMegabytes:100});await req('/api/server/restart',{});let bridge;
 for(let n=0;n<150;n++){bridge=await req('/api/bridge');if(bridge.connected)break;await new Promise(r=>setTimeout(r,200));}assert.equal(bridge.connected,true,JSON.stringify(bridge));console.log('PASS '+backend+' authenticated owned LAN bridge');
 const commandReply=await req('/api/command',{text:'echo actual-bridge-stream'});let diagnostic;for(let n=0;n<50;n++){diagnostic=await req('/api/diagnostics?source=all');if(diagnostic.lines.some(x=>x.sourceSeq!==null&&x.text.includes('actual-bridge-stream')))break;await new Promise(r=>setTimeout(r,200));}assert(diagnostic.lines.some(x=>x.sourceSeq!==null&&x.text.includes('actual-bridge-stream')),JSON.stringify({commandReply,diagnostic}));assert((await req('/api/bundles')).ok);console.log('PASS '+backend+' real bridge console, logs and bundle query');
 await req('/api/settings',{shutdownMode:'keep-running'});await stopWeb();restartWeb();for(let n=0;n<60;n++){try{csrf=(await req('/api/session')).csrf;break;}catch{}await new Promise(r=>setTimeout(r,200));}assert.equal((await req('/api/bridge')).connected,true);console.log('PASS '+backend+' bridge survives Webhost keep-running restart');
 await req('/api/settings',{shutdownMode:'stop-server'});await stopWeb();restartWeb();for(let n=0;n<60;n++){try{csrf=(await req('/api/session')).csrf;break;}catch{}await new Promise(r=>setTimeout(r,200));}assert.equal((await req('/api/status')).status.state,'stopped');await req('/api/server/start',{});for(let n=0;n<100;n++){bridge=await req('/api/bridge');if(bridge.connected)break;await new Promise(r=>setTimeout(r,200));}assert.equal(bridge.connected,true);console.log('PASS '+backend+' bridge reconnects after stop-server shutdown');
 await req('/api/bridge/settings',{enabled:false,port:30774,retentionDays:7,maxMegabytes:100});await req('/api/server/restart',{});await new Promise(r=>setTimeout(r,1800));assert.equal((await req('/api/bridge')).connected,false);let fallbackBundles;for(let n=0;n<30;n++){fallbackBundles=await req('/api/bundles');if(fallbackBundles.ok)break;await new Promise(r=>setTimeout(r,250));}assert(fallbackBundles.ok,JSON.stringify(fallbackBundles));console.log('PASS '+backend+' bridge disable restores PTY');
 await req('/api/server/stop',{});console.log('PASS downloaded Replay stops gracefully');
}catch(error){try{console.log(JSON.stringify(await req('/api/logs')));}catch{}throw error;}finally{supervisor?.kill();web?.kill();}
