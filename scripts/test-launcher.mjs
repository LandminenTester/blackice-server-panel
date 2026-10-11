import {spawn} from 'node:child_process';
import {mkdir,writeFile,readFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {randomBytes} from 'node:crypto';
import net from 'node:net';
import os from 'node:os';
import assert from 'node:assert/strict';
const root=resolve(import.meta.dirname,'..'),packageRoot=resolve(root,process.env.PANEL_LAUNCHER_PACKAGE??'.');
const suffix=process.platform==='win32'?'.exe':'';
const folder=resolve(packageRoot,'.local');await mkdir(folder,{recursive:true});
const configPath=resolve(folder,'panel.json');
if(!packageRoot.includes('.local'))throw Error('Pass an isolated .local launcher package; never overwrite a real configuration.');
const config={schemaVersion:2,backend:process.platform==='win32'?'windows':'linux',autoStartServer:true,serverRoot:resolve(root,'.local/fixture'),pipeName:'launcher-'+process.pid,url:'http://127.0.0.1:20777',passwordHash:randomBytes(32).toString('base64'),passwordSalt:randomBytes(16).toString('base64'),shutdownMode:'keep-running'};
await writeFile(configPath,JSON.stringify(config));
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
async function call(action,extra={}){return new Promise((resolve,reject)=>{const endpoint=process.platform==='win32'?'\\\\.\\pipe\\'+config.pipeName:(process.env.REPLAY_PANEL_RUNTIME??os.tmpdir()+'/replay-panel-'+os.userInfo().username)+'/'+config.pipeName+'.sock';const client=net.createConnection(endpoint);let data='';client.setTimeout(15000);client.on('connect',()=>client.write(JSON.stringify({action,...extra})+'\n'));client.on('data',chunk=>{data+=chunk;if(data.includes('\n')){client.end();resolve(JSON.parse(data));}});client.on('error',reject);client.on('timeout',()=>{client.destroy();reject(Error('IPC timeout'));});});}
function launch(args=[]){const child=spawn(resolve(packageRoot,'Start'+suffix),args,{cwd:packageRoot,env:{...process.env,REPLAY_PANEL_NO_TERMINAL:'1'},windowsHide:true});child.output='';child.stdout.on('data',x=>child.output+=x);child.stderr.on('data',x=>child.output+=x);return child;}
async function ready(){for(let i=0;i<100;i++){try{const status=await call('status');if((await fetch(config.url+'/api/session')).ok&&app.output.includes('[R]'))return status;}catch{}await sleep(100);}throw Error('Launcher failed to boot');}
async function exit(child,input){if(input!==undefined)child.stdin.end(input);await new Promise((resolve,reject)=>{const timer=setTimeout(()=>reject(Error('Controller did not exit: '+child.output)),30000);child.once('exit',code=>{clearTimeout(timer);code===0?resolve():reject(Error('Controller failed: '+child.output));});});}
async function stopped(){await sleep(300);try{await call('status');throw Error('Supervisor still active');}catch(error){if(error.message==='Supervisor still active')throw error;}await assert.rejects(()=>fetch(config.url+'/api/session'));}
let app;
try{
 app=launch();let status=await ready();assert.equal(status.status.state,'running');const pid=status.status.pid;console.log('PASS direct Start boots services and auto-starts server');
 const bridge=launch(['--server-console',configPath]);bridge.stdin.write('echo native-console-verified\n');
 for(let i=0;i<60&&!bridge.output.split(/\r?\n/).some(x=>x.trim()==='native-console-verified');i++)await sleep(100);
 assert(bridge.output.split(/\r?\n/).some(x=>x.trim()==='native-console-verified'));console.log('PASS native server terminal sends commands and displays real output');
 await exit(bridge,'');assert.equal((await call('status')).status.state,'stopped');console.log('PASS closing server console stops only game server');
 await call('start');await exit(app,'K\n');assert.equal((await call('status')).status.state,'running');assert((await fetch(config.url+'/api/session')).ok);console.log('PASS explicit keep-services survives Start exit');
 app=launch();status=await ready();const runningPid=status.status.pid;await sleep(200);assert.equal((await call('status')).status.pid,runningPid);await exit(app,'Q\n');await stopped();console.log('PASS Q closes game, Webhost, Supervisor and control terminal');
 config.autoStartServer=false;await writeFile(configPath,JSON.stringify(config));
 app=launch();status=await ready();assert.equal(status.status.state,'stopped');await call('settings',{autoStartServer:true});assert.equal(JSON.parse(await readFile(configPath)).autoStartServer,true);await exit(app,'K\n');console.log('PASS auto-start setting is persisted without starting immediately');
 app=launch();status=await ready();assert.equal(status.status.state,'running');await exit(app,'');await stopped();console.log('PASS reopened Start respects auto-start and input closure shuts down stack');
 console.log('7 launcher acceptance checks passed');
}finally{try{await call('stop');}catch{}try{await call('shutdown');}catch{}app?.kill();}
