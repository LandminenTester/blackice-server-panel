import {spawn} from 'node:child_process';
import {mkdir,readFile,writeFile,copyFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {randomBytes} from 'node:crypto';
import assert from 'node:assert/strict';
const root=resolve(import.meta.dirname,'..'),build=process.env.PANEL_TEST_BUILD??'artifacts',directory=resolve(root,'.local/setup-'+randomBytes(6).toString('hex'));
await mkdir(directory,{recursive:true});
const configPath=resolve(directory,'panel.json'),config={schemaVersion:2,autoStartServer:false,url:'http://127.0.0.1:20775',pipeName:'panel-setup-'+process.pid,backend:process.platform==='win32'?'windows':'linux'};
await writeFile(configPath,JSON.stringify(config));
const suffix=process.platform==='win32'?'.exe':'',fixture=resolve(directory,'Server With Spaces');
await mkdir(resolve(fixture,'cfg'),{recursive:true});await writeFile(resolve(fixture,'cfg/server.cfg'),'# disposable test');
await copyFile(resolve(root,'.local/fixture/REServer'+suffix),resolve(fixture,'REServer'+suffix));
let output='',key='',csrf='',cookies=new Map(),count=0;
const supervisor=spawn(resolve(root,build,'supervisor/Supervisor'+suffix),[configPath],{env:{...process.env,REPLAY_PANEL_NO_TERMINAL:'1'},stdio:['ignore','pipe','inherit']});
supervisor.stdout.on('data',data=>{output+=data;key=output.match(/Setup key: ([A-F0-9]+)/)?.[1]??'';});
const web=spawn(resolve(root,build,'web/Web'+suffix),[],{cwd:resolve(root,build,'web'),env:{...process.env,REPLAY_PANEL_CONFIG:configPath},stdio:'ignore'});
async function request(route,body){const response=await fetch(config.url+route,{method:body?'POST':'GET',headers:{Cookie:[...cookies].map(x=>x.join('=')).join('; '),'Content-Type':'application/json','X-CSRF':csrf},body:body?JSON.stringify(body):undefined});for(const cookie of response.headers.getSetCookie()){const [name,...value]=cookie.split(';')[0].split('=');cookies.set(name,value.join('='));}return response;}
function check(value,name){assert(value,name);console.log('PASS '+name);count++;}
try{
for(let i=0;i<80;i++){try{const response=await request('/api/session');csrf=(await response.json()).csrf;if(key)break;}catch{}await new Promise(r=>setTimeout(r,200));}
check(!(await(await request('/api/bootstrap')).json()).configured,'fresh host boots into protected setup');
check(key.length===6,'setup key has 6 characters');
check((await(await request('/api/setup/console')).json()).join(' ').includes('Setup key: '+key),'setup key shown in web terminal');
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
const saved=JSON.parse(await readFile(configPath));check(saved.passwordHash&&saved.passwordSalt&&!JSON.stringify(saved).includes(account.password),'password is stored only as salted hash');
console.log(count+' setup checks passed');
}finally{web.kill();supervisor.kill();}
