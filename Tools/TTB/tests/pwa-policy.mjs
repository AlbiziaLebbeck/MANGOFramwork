import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile,writeFile,mkdir,mkdtemp } from 'node:fs/promises';
import { resolve } from 'node:path';
import { createHash,webcrypto } from 'node:crypto';
import { runInNewContext } from 'node:vm';
import { packagePwa } from '../package-pwa.mjs';
const repository=resolve(import.meta.dirname,'../../..');
const sha=bytes=>createHash('sha256').update(bytes).digest('hex');
async function worker({corrupt=false,edge=false}={}){
  const content=new Map([['index.html','<html>static shell</html>'],['mango-ttb.js','/* public IIFE */']]);
  const files=[...content].map(([path,body])=>({path,...(edge&&path==='index.html'?{source:'mango-ttb-shell.json'}:{}),size:Buffer.byteLength(body),sha256:sha(body)}));
  const listeners=new Map(),storage=new Map();let activated=0,claimed=0,network=0;
  const cacheApi={async open(name){if(!storage.has(name))storage.set(name,new Map());const items=storage.get(name);return {async put(key,value){items.set(String(key),value.clone())},async match(key){return items.get(String(key))?.clone()}}},async keys(){return [...storage.keys()]},async delete(name){return storage.delete(name)}};
  const self={location:{href:'https://shop.test/app/mango-ttb-sw.js'},clients:{async claim(){claimed++}},addEventListener(name,callback){listeners.set(name,callback)},async skipWaiting(){activated++}};
  const fetch=async value=>{network++;const url=new URL(typeof value==='string'?value:value.url??value.href);const file=url.pathname.replace('/app/','');
    assert.equal(url.searchParams.get('__mangos_release'),'a'.repeat(64));
    let body=content.get(file);if(edge&&file==='mango-ttb-shell.json')body=JSON.stringify({html:corrupt?'tampered shell':content.get('index.html')});
    if(edge&&file==='index.html')body+='<script>/* hosting analytics injection */</script>';
    return new Response(corrupt&&file!=='mango-ttb-shell.json'?'corrupt':body??'not-found',{status:body!==undefined?200:404,headers:{'Content-Encoding':'gzip','Content-Length':'999','Vary':'Accept-Encoding','Content-Type':file.endsWith('.js')?'application/javascript':file.endsWith('.json')?'application/json':'text/html'}})};
  const source=(await readFile(resolve(repository,'WebCommerce/pwa/service-worker.template.js'),'utf8')).replace('__RELEASE__',JSON.stringify('a'.repeat(64))).replace('__MANIFEST__',JSON.stringify(files));
  runInNewContext(source,{self,URL,Response,Request,Headers,caches:cacheApi,crypto:webcrypto,fetch,Map,Uint8Array,TextDecoder,TextEncoder,Date,Error});
  async function lifecycle(name){let pending;listeners.get(name)({waitUntil(value){pending=value}});await pending}
  async function request(path,method='GET',mode='cors'){let pending;listeners.get('fetch')({request:{url:'https://shop.test'+path,method,mode},respondWith(value){pending=value}});return pending?await pending:null}
  return {listeners,storage,cacheApi,lifecycle,request,get activated(){return activated},get claimed(){return claimed},get network(){return network}};
}
test('shell caching excludes every financial/private request, Unity runtime and non-GET method',async()=>{
  const sw=await worker();await sw.lifecycle('install');assert.equal(sw.activated,0);await sw.lifecycle('activate');assert.equal(sw.claimed,1);
  for(const path of ['/ttb-commerce/v1/session','/ttb-commerce/v1/orders','/ttb-commerce-test/v1/inventory/owned/download','/ttb/v1/credit-notification','/app/Build/client.wasm','/app/StreamingAssets/model.glb','/app/ttb-commerce-config.js','/app/mango-ttb.js?token=private'])assert.equal(await sw.request(path),null,path);
  assert.equal(await sw.request('/app/mango-ttb.js','POST'),null);const script=await sw.request('/app/mango-ttb.js');assert.equal(script.status,200);assert.equal(script.headers.has('Content-Encoding'),false);assert.equal(script.headers.has('Content-Length'),false);assert.equal(script.headers.has('Vary'),false);assert.equal(script.headers.get('Content-Type'),'application/javascript');
  assert.equal(await (await sw.request('/app/mango-ttb.js?__mangos_release='+'a'.repeat(64))).text(),'/* public IIFE */');
  assert.equal(await sw.request('/app/mango-ttb.js?__mangos_release='+'b'.repeat(64)),null);
  assert.equal(await sw.request('/app/mango-ttb.js?__mangos_release='+'a'.repeat(64)+'&token=private'),null);
  const before=sw.network;assert.equal(await (await sw.request('/app/','GET','navigate')).text(),'<html>static shell</html>');assert.equal(sw.network,before);
});
test('updates wait for an explicit in-scope activation message and reject a mismatched release',async()=>{
  const sw=await worker();await sw.lifecycle('install');assert.equal(sw.activated,0);
  let version;sw.listeners.get('message')({data:{type:'GET_VERSION'},ports:[{postMessage(value){version=value}}]});assert.equal(version.release,'a'.repeat(64));
  sw.listeners.get('message')({data:{type:'ACTIVATE'},source:{url:'https://foreign.test/app/'},waitUntil(){throw new Error('Foreign activation')}});assert.equal(sw.activated,0);
  let pending;sw.listeners.get('message')({data:{type:'ACTIVATE'},source:{url:'https://shop.test/app/'},waitUntil(value){pending=value}});await pending;assert.equal(sw.activated,1);
  const broken=await worker({corrupt:true});await broken.cacheApi.open('unrelated-site-cache');await assert.rejects(()=>broken.lifecycle('install'),/release mismatch/);assert.deepEqual(await broken.cacheApi.keys(),['unrelated-site-cache']);assert.equal(broken.activated,0);
});
test('hosting-injected HTML cannot block an intact canonical shell or bypass its hash',async()=>{
  const sw=await worker({edge:true});await sw.lifecycle('install');await sw.lifecycle('activate');
  const shell=await sw.request('/app/','GET','navigate');assert.equal(await shell.text(),'<html>static shell</html>');
  assert.equal(shell.headers.get('Content-Type'),'text/html; charset=utf-8');assert.equal(sw.activated,0);
  const broken=await worker({edge:true,corrupt:true});await assert.rejects(()=>broken.lifecycle('install'),/release mismatch/);assert.deepEqual(await broken.cacheApi.keys(),[]);
});
test('the generated package is repeatable, digest-verified and contains only the public shell',async()=>{
  const artifacts=resolve(repository,'.artifacts');await mkdir(artifacts,{recursive:true});const directory=await mkdtemp(resolve(artifacts,'pwa-package-test-'));
  await writeFile(resolve(directory,'index.html'),'<!doctype html><html><head></head><body><script src="./mango-ttb.js"></script></body></html>');await writeFile(resolve(directory,'mango-ttb.js'),'/* public code */');await writeFile(resolve(directory,'mango-ttb.css'),'body{color:#233d33}');
  await mkdir(resolve(directory,'Build'));await writeFile(resolve(directory,'Build/private-runtime.wasm'),'must not cache');
  await writeFile(resolve(directory,'ttb-viewport.css'),'#unity-canvas{top:64px}');
  const first=await packagePwa(directory);const again=await packagePwa(directory);assert.equal(first.release,again.release);
  const manifest=JSON.parse(await readFile(resolve(directory,'pwa-release.json'),'utf8'));assert.equal(manifest.files.some(file=>file.path.startsWith('Build/')),false);
  assert.equal(manifest.files.some(file=>file.path==='ttb-viewport.css'),true);
  for(const file of manifest.files){const bytes=await readFile(resolve(directory,file.path));assert.equal(bytes.length,file.size);assert.equal(sha(bytes),file.sha256)}
  const html=await readFile(resolve(directory,'index.html'),'utf8');assert.equal((html.match(/name="mango-ttb-release"/g)??[]).length,1);assert.equal((html.match(/rel="manifest"/g)??[]).length,1);
  assert.ok(html.includes('mango-ttb.js?__mangos_release='+manifest.release));
  const index=manifest.files.find(file=>file.path==='index.html');assert.equal(index.source,'mango-ttb-shell.json');
  assert.equal(JSON.parse(await readFile(resolve(directory,index.source),'utf8')).html,html);
});
