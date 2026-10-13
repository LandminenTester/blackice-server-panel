import {spawn} from 'node:child_process';
import {mkdir,readFile,writeFile,copyFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {randomBytes} from 'node:crypto';
import assert from 'node:assert/strict';
import zlib from 'node:zlib';
const root=resolve(import.meta.dirname,'..'),build=process.env.PANEL_TEST_BUILD??'artifacts',directory=resolve(root,'.local/setup-'+randomBytes(6).toString('hex'));
await mkdir(directory,{recursive:true});
const configPath=resolve(directory,'panel.json'),config={schemaVersion:2,autoStartServer:false,url:'http://127.0.0.1:20775',pipeName:'panel-setup-'+process.pid,backend:process.platform==='win32'?'windows':'linux'};
await writeFile(configPath,JSON.stringify(config));
const suffix=process.platform==='win32'?'.exe':'',fixture=resolve(directory,'Server With Spaces');
await mkdir(resolve(fixture,'cfg'),{recursive:true});await writeFile(resolve(fixture,'cfg/server.cfg'),'# disposable test');
await copyFile(resolve(root,'.local/fixture/REServer'+suffix),resolve(fixture,'REServer'+suffix));
let output='',key='',csrf='',cookies=new Map(),count=0;
const supervisor=spawn(resolve(root,build,'supervisor/Supervisor'+suffix),[configPath],{env:{...process.env,REPLAY_PANEL_NO_TERMINAL:'1'},stdio:['ignore','pipe','inherit']});
supervisor.stdout.on('data',data=>{output+=data;key=[...output.matchAll(/Setup key: ([A-Z0-9]+)/g)].pop()?.[1]??'';});
const web=spawn(resolve(root,build,'web/Web'+suffix),[],{cwd:resolve(root,build,'web'),env:{...process.env,REPLAY_PANEL_CONFIG:configPath},stdio:'ignore'});
async function request(route,body){const response=await fetch(config.url+route,{method:body?'POST':'GET',headers:{Cookie:[...cookies].map(x=>x.join('=')).join('; '),'Content-Type':'application/json','X-CSRF':csrf},body:body?JSON.stringify(body):undefined});for(const cookie of response.headers.getSetCookie()){const [name,...value]=cookie.split(';')[0].split('=');cookies.set(name,value.join('='));}return response;}
function check(value,name){assert(value,name);console.log('PASS '+name);count++;}
try{
for(let i=0;i<80;i++){try{const response=await request('/api/session');csrf=(await response.json()).csrf;if(key)break;}catch{}await new Promise(r=>setTimeout(r,200));}
check(!(await(await request('/api/bootstrap')).json()).configured,'fresh host boots into protected setup');
check(key.length===8,'setup key has 8 characters');
check((await(await request('/api/setup/console')).json()).join(' ').includes('Setup key: '+key),'setup key shown in web terminal for local requests');
const proxied=await(await fetch(config.url+'/api/setup/console',{headers:{'X-Forwarded-For':'203.0.113.9'}})).json();check(!proxied.join(' ').includes(key)&&proxied.join(' ').includes('hidden'),'setup key hidden from proxied requests');
const lowercase=key.toLowerCase();check((await request('/api/setup/validate',{key:lowercase,serverRoot:'relative',backend:config.backend})).status===409,'lowercase key accepted before path validation');
const oldKey=key;for(let i=0;i<5;i++)await request('/api/setup/validate',{key:'WRONG'+i,serverRoot:fixture,backend:config.backend});
for(let i=0;i<40&&key===oldKey;i++)await new Promise(r=>setTimeout(r,100));
check(key!==oldKey&&key.length===8,'setup key regenerated after repeated wrong keys');
check((await request('/api/setup/pick',{key:'wrong',path:''})).status===409,'wrong setup key rejected');
check(String((await(await request('/api/bootstrap')).json()).defaultServerRoot).endsWith('server'),'default server folder offered by bootstrap');
check((await request('/api/setup/validate',{key,serverRoot:'relative',backend:config.backend})).status===409,'relative path rejected');
check((await request('/api/setup/validate',{key,serverRoot:fixture,backend:config.backend})).ok,'executable and config accepted in path with spaces');
check((await request('/api/setup/install',{key,serverRoot:fixture,backend:config.backend})).status===409,'installer preserves nonempty directory');
const account={key,dashboardName:'Test Dashboard',username:'test-admin',password:'test-password-long',confirmation:'test-password-long',locale:'en',serverRoot:fixture,backend:config.backend};
check((await request('/api/setup/finish',{...account,confirmation:'mismatch'})).status===409,'password mismatch rejected');
check((await request('/api/setup/finish',{...account,password:'short12',confirmation:'short12'})).status===409,'password below 8 characters rejected');
const replies=await Promise.all([request('/api/setup/finish',account),request('/api/setup/finish',account)]);
check(replies.filter(x=>x.ok).length===1,'only one concurrent setup completion succeeds');
check((await request('/api/setup/console')).status===409,'web terminal key hidden after completion');
check((await request('/api/setup/validate',{key,serverRoot:fixture,backend:config.backend})).status===409,'setup endpoints locked after completion');
check((await request('/api/login',{username:'other',password:account.password})).status===401,'wrong username rejected');
check((await request('/api/login',{username:account.username,password:account.password})).ok,'new admin can sign in');
csrf=(await(await request('/api/session')).json()).csrf;const remembered=await request('/api/login',{username:account.username,password:account.password,remember:true});check(remembered.ok&&/expires=/i.test(remembered.headers.getSetCookie().join(';')),'stay signed in issues a persistent cookie');
check((await(await request('/api/server-name')).json()).name!==undefined,'server name endpoint available to signed-in users');
csrf=(await(await request('/api/session')).json()).csrf;
check((await(await request('/api/settings',{updateChannel:'experimental'})).json()).status?.updateChannel==='experimental','update channel can be switched to experimental');
check((await request('/api/settings',{updateChannel:'nightly'})).status===409,'unknown update channel rejected');
check((await(await request('/api/settings',{updateChannel:'stable'})).json()).status?.updateChannel==='stable','update channel returns to stable');
function storedZip(files){const parts=[],central=[];let offset=0;for(const [name,text] of Object.entries(files)){const data=Buffer.from(text),nameBuf=Buffer.from(name),crc=zlib.crc32(data);const local=Buffer.alloc(30);local.writeUInt32LE(0x04034b50,0);local.writeUInt16LE(20,4);local.writeUInt32LE(crc>>>0,14);local.writeUInt32LE(data.length,18);local.writeUInt32LE(data.length,22);local.writeUInt16LE(nameBuf.length,26);parts.push(local,nameBuf,data);const entry=Buffer.alloc(46);entry.writeUInt32LE(0x02014b50,0);entry.writeUInt16LE(20,4);entry.writeUInt16LE(20,6);entry.writeUInt32LE(crc>>>0,16);entry.writeUInt32LE(data.length,20);entry.writeUInt32LE(data.length,24);entry.writeUInt16LE(nameBuf.length,28);entry.writeUInt32LE(offset,42);central.push(entry,nameBuf);offset+=30+nameBuf.length+data.length;}const dir=Buffer.concat(central);const end=Buffer.alloc(22);end.writeUInt32LE(0x06054b50,0);end.writeUInt16LE(Object.keys(files).length,8);end.writeUInt16LE(Object.keys(files).length,10);end.writeUInt32LE(dir.length,12);end.writeUInt32LE(offset,16);return Buffer.concat([...parts,dir,end]);}
async function upload(bytes,name){const form=new FormData();form.append('file',new Blob([bytes]),name);const response=await fetch(config.url+'/api/bundle-tools/upload',{method:'POST',headers:{Cookie:[...cookies].map(x=>x.join('=')).join('; '),'X-CSRF':csrf},body:form});return {status:response.status,body:await response.json().catch(()=>null)};}
csrf=(await(await request('/api/session')).json()).csrf;
const demoManifest=['[General]','description = "API test bundle"','author = "ci"','version = "0.1.0"','[Scripting]','server_blob = "sv.wasm"','[Meta]','config_version = "alpha_playtest"',''].join(String.fromCharCode(10));
const demoZip=storedZip({'config.toml':demoManifest,'sv.wasm':'wasm'});
check((await upload(demoZip,'api-bundle.zip')).status===409,'bundle upload is refused while bundle tools are off');
check((await request('/api/bundle-tools/settings',{enabled:true,uploadEnabled:true})).ok,'bundle tools can be enabled (experimental)');
const uploaded=await upload(demoZip,'api-bundle.zip');
check(uploaded.status===200&&uploaded.body.report.ok&&uploaded.body.report.name==='api-bundle'&&uploaded.body.exists===false,'bundle ZIP upload is inspected');
const bundleJob=await(await request('/api/bundle-tools/upload/install',{file:uploaded.body.file,originalName:'api-bundle.zip',name:'api-bundle',replace:false})).json();
let bundleState=bundleJob;for(let i=0;i<100&&bundleState.state==='running';i++){await new Promise(r=>setTimeout(r,100));bundleState=(await(await request('/api/bundle-tools/jobs/'+bundleJob.id)).json()).job;}
check(bundleState.state==='succeeded'&&(await readFile(resolve(fixture,'data/ugc/api-bundle/config.toml'),'utf8')).includes('API test bundle'),'uploaded bundle is installed into data/ugc');
check((await upload(Buffer.from('not a zip at all, definitely not'),'bad.zip')).status===409,'a non-ZIP upload is refused');
check((await request('/api/bundle-tools/settings',{enabled:false})).ok,'bundle tools can be switched off again');
await mkdir(resolve(fixture,'logs'),{recursive:true});await writeFile(resolve(fixture,'logs/server_2026-10-12T09-03-05Z_cl0.txt'),'hello log');
const listed=await(await request('/api/log-files')).json();check(listed.length===1&&listed[0].started?.startsWith('2026-10-12T09:03:05'),'log archive lists server starts');
check((await(await request('/api/log-files/server_2026-10-12T09-03-05Z_cl0.txt')).text())==='hello log','log file readable');
check((await request('/api/log-files/..%2Fpanel.json')).status===404,'log path traversal rejected');
const toml=['[General]','description = "Demo \\"mode\\" bundle"','author = "replay.re"','version = "1.0.0"','','[Meta]','config_version = "alpha_playtest"',''].join(String.fromCharCode(10));await mkdir(resolve(fixture,'data/ugc/demo'),{recursive:true});await writeFile(resolve(fixture,'data/ugc/demo/config.toml'),toml);
const bundleList=await(await request('/api/bundles')).json();const demo=bundleList.bundles?.find(b=>b.name==='demo');check(demo?.author==='replay.re'&&demo?.version==='1.0.0'&&demo?.description==='Demo "mode" bundle','bundle author, version and description are read from config.toml');
const current=await request('/api/log-files-current');check(current.ok&&(await current.text())==='hello log'&&/attachment/i.test(current.headers.get('content-disposition')??''),'current server start log downloads as one file');
const zip=Buffer.from(await(await request('/api/log-files.zip')).arrayBuffer());check(zip.subarray(0,2).toString()==='PK'&&zip.includes('server_2026-10-12T09-03-05Z_cl0.txt'),'all logs download as ZIP');
const cfgBefore=await(await request('/api/server-config')).json();check(cfgBefore.exists&&cfgBefore.values.autoUpdate===false,'server config readable after setup');
const saved=JSON.parse(await readFile(configPath));check(saved.passwordHash&&saved.passwordSalt&&!JSON.stringify(saved).includes(account.password),'password is stored only as salted hash');
console.log(count+' setup checks passed');
}finally{web.kill();supervisor.kill();}
