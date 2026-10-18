import {execFileSync} from 'node:child_process';
import {randomBytes,pbkdf2Sync} from 'node:crypto';
import assert from 'node:assert/strict';
const prefix='panel-updater-'+randomBytes(5).toString('hex'),instance=randomBytes(16).toString('hex');
const docker=(...args)=>execFileSync('docker',args,{encoding:'utf8'}).trim();
const image='replay-panel:updates-test',url='http://127.0.0.1:20778',volumes=['state','runtime','server'].map(x=>prefix+'-'+x);
const gid=docker('run','--rm','--user','0','--entrypoint','stat','-v','/var/run/docker.sock:/var/run/docker.sock',image,'-c','%g','/var/run/docker.sock');
const containers=[];
// Read the exact source version rather than relying on a globally installed image tag.
const {readFile}=await import('node:fs/promises');
const target=(await readFile(new URL('../Directory.Build.props',import.meta.url),'utf8')).match(/<Version>([^<]+)<\/Version>/)[1];
const salt=randomBytes(16),password='isolated-docker-updater-test';
const config={schemaVersion:2,backend:'docker',instanceId:instance,containerId:'',serverRoot:'/server',autoStartServer:false,shutdownMode:'keep-running',pipeName:prefix,url:'http://0.0.0.0:20772',allowedHosts:['127.0.0.1:20778'],passwordSalt:salt.toString('base64'),passwordHash:pbkdf2Sync(password,salt,210000,32,'sha256').toString('base64')};
let csrf='',cookies=new Map();
async function request(path,body){const response=await fetch(url+path,{method:body?'POST':'GET',headers:{'Content-Type':'application/json','X-CSRF':csrf,Cookie:[...cookies].map(x=>x.join('=')).join('; ')},body:body?JSON.stringify(body):undefined});for(const value of response.headers.getSetCookie()){const pair=value.split(';')[0],index=pair.indexOf('=');cookies.set(pair.slice(0,index),pair.slice(index+1));}assert(response.ok,`${path}: ${response.status} ${await response.clone().text()}`);return response.json();}
async function login(){cookies.clear();csrf=(await request('/api/session')).csrf;await request('/api/login',{username:'admin',password});csrf=(await request('/api/session')).csrf;return true;}
async function waitFor(check,seconds=90){let last;for(let n=0;n<seconds*4;n++){try{const value=await check();if(value)return value;}catch(e){last=e;}await new Promise(r=>setTimeout(r,250));}throw last??Error('Timed out');}
function container(name,args){containers.push(name);return docker('run','-d','--name',name,...args);}
try{
 for(const volume of volumes)docker('volume','create',volume);
 docker('run','--rm','--user','0','--entrypoint','/app/docker-init.sh','-v',volumes[0]+':/state','-v',volumes[1]+':/run/replay-panel','-v',volumes[2]+':/server',image);
 const game=container(prefix+'-game',['--user','0:0','-i','-t','--label','replay.panel.instance='+instance,'-v',volumes[2]+':/srv','--entrypoint','sh',image,'-c','mkdir -p /srv/cfg /srv/settings; printf \'sv_name "Fixture"\\nsv_region "DEU"\\n\' > /srv/cfg/server.cfg; printf \'{"auto_update":false}\' > /srv/settings/server.settings.json; while IFS= read -r line; do printf "fixture-response:%s\\n" "$line"; done']);
 config.containerId=game;
 docker('run','--rm','--user','0','--entrypoint','sh','-v',volumes[0]+':/state',image,'-c','printf %s "$1" > /state/panel.json; chown 10001:10001 /state/panel.json; chmod 600 /state/panel.json','sh',JSON.stringify(config));
 const env=['-e','REPLAY_PANEL_CONFIG=/state/panel.json','-e','REPLAY_PANEL_RUNTIME=/run/replay-panel'];
 const labels=['--label','com.docker.compose.project='+prefix];
 const supervisor=container(prefix+'-supervisor',[...env,...labels,'--label','replay.panel.role=supervisor','--user','10001:'+gid,'-e','REPLAY_PANEL_DOCKER_UPDATES=1','-v',volumes[0]+':/state','-v',volumes[1]+':/run/replay-panel','-v',volumes[2]+':/server','-v','/var/run/docker.sock:/var/run/docker.sock','--entrypoint','/app/docker-entrypoint.sh',image,'supervisor','/state/panel.json']);
 container(prefix+'-web',[...env,...labels,'--label','replay.panel.role=web','-v',volumes[0]+':/state','-v',volumes[1]+':/run/replay-panel','-p','127.0.0.1:20778:20772',image]);
 await waitFor(login);assert.equal((await request('/api/updates')).canInstall,true);
 const values=(await request('/api/server-config')).values;
 docker('exec',game,'sh','-c','chown root:root /srv/settings /srv/settings/server.settings.json; chmod 700 /srv/settings; chmod 600 /srv/settings/server.settings.json');
 await request('/api/server-config',values);assert.deepEqual((await request('/api/server-config')).values,values);console.log('PASS root-owned Docker config save without manual chmod');
 docker('restart',game);await request('/api/command',{text:'echo reconnect-verified'});
 await waitFor(()=>docker('logs',game).includes('fixture-response:echo reconnect-verified'));console.log('PASS actual command response after external game restart');
 const gameStart=docker('inspect','--format','{{.State.StartedAt}}',game);
 async function apply(broken=false){
  const id=randomBytes(16).toString('hex'),stage='/state/panel-updates/'+id+'/stage';
  docker('exec','--user','0',supervisor,'sh','-c','mkdir -p "$1"; cp -a /app/artifacts "$1/"; cp /app/artifacts/supervisor/Supervisor "$1/Start"; printf %s "$2" > "$1/VERSION"; if [ "$3" = broken ]; then cp /bin/false "$1/artifacts/web/Web"; fi; chown -R 10001:10001 "$(dirname "$1")"','sh',stage,target,broken?'broken':'good');
  docker('exec',supervisor,'/test-runner/DockerUpdates.Tests','/state/panel.json',id,target);
  await waitFor(async()=>{const info=await request('/api/updates');return info.job?.id===id&&['completed','failed'].includes(info.job?.state)&&info;},150);
  await login();return{id,info:await request('/api/updates')};
 }
 const good=await apply();assert.equal(good.info.job.state,'completed');assert.equal(good.info.current,target);assert.equal(docker('inspect','--format','{{.State.StartedAt}}',game),gameStart);console.log('PASS Docker panel runtime update, health verification and unchanged game');
 const bad=await apply(true);assert.equal(bad.info.job.state,'failed');assert.equal(bad.info.job.error,'update.restored');assert.equal(docker('exec',supervisor,'cat','/state/panel-active-runtime'),good.id);assert.equal(docker('inspect','--format','{{.State.StartedAt}}',game),gameStart);console.log('PASS failed Docker panel startup rolls back without restarting game');
 docker('restart',supervisor);await waitFor(login);assert.equal(docker('inspect','--format','{{.State.StartedAt}}',game),gameStart);assert.equal((await request('/api/updates')).current,target);console.log('PASS persisted runtime and keep-running across Supervisor restart');
}catch(e){for(const name of containers){try{console.error(name,docker('logs','--tail','30',name));}catch{}}throw e;}
finally{const helpers=docker('ps','-aq','--filter','label=replay.panel.instance='+instance,'--filter','label=replay.panel.update-helper=true').split(/\s+/).filter(Boolean);for(const id of helpers){try{docker('rm','-f',id);}catch{}}for(const name of containers.reverse()){try{docker('rm','-f',name);}catch{}}for(const volume of volumes){try{docker('volume','rm',volume);}catch{}}}
