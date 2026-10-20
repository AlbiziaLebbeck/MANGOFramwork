/* Generated from a public-static-file manifest. Financial and Unity runtime requests remain network-only. */
const RELEASE=__RELEASE__;
const FILES=__MANIFEST__;
const ROOT=new URL('./',self.location.href);
const PREFIX='mango-ttb-shell:'+encodeURIComponent(ROOT.pathname)+':';
const CACHE=PREFIX+RELEASE;
const INDEX=new URL('index.html',ROOT).href;
const META=new URL('.ttb-pwa-cache-meta',ROOT).href;
const PUBLIC=new Map(FILES.map(file=>[new URL(file.path,ROOT).href,file]));
function financial(url){return /\/(?:ttb-commerce(?:-test)?\/v1|ttb\/v1)(?:\/|$)/.test(url.pathname)}
async function hash(bytes){return [...new Uint8Array(await crypto.subtle.digest('SHA-256',bytes))].map(value=>value.toString(16).padStart(2,'0')).join('')}
self.addEventListener('install',event=>event.waitUntil((async()=>{
  const cache=await caches.open(CACHE);
  try{
    for(const file of FILES){
      const url=new URL(file.path,ROOT);if(url.origin!==ROOT.origin||financial(url)||url.search||file.path.startsWith('Build/'))throw new Error('Invalid public shell manifest');
      if(file.source&&(file.path!=='index.html'||file.source!=='mango-ttb-shell.json'))throw new Error('Invalid shell source');
      const download=new URL(file.source??file.path,ROOT);
      // A release-specific URL avoids a CDN returning an older same-named IIFE for hours.
      download.searchParams.set('__mangos_release',RELEASE);
      const response=await fetch(download,{cache:'no-store',credentials:'omit',redirect:'error'});
      if(!response.ok||response.type==='opaque')throw new Error('Public shell asset unavailable');
      let bytes=await response.arrayBuffer();
      if(file.source){
        if(bytes.byteLength>file.size*6+128)throw new Error('Public shell envelope too large');
        const shell=JSON.parse(new TextDecoder('utf-8',{fatal:true}).decode(bytes));
        if(!shell||typeof shell.html!=='string'||Object.keys(shell).length!==1)throw new Error('Invalid public shell envelope');
        bytes=new TextEncoder().encode(shell.html);
      }
      if(bytes.byteLength!==file.size||await hash(bytes)!==file.sha256)throw new Error('Public shell release mismatch');
      // fetch exposes decoded bytes. Transport encoding/length and negotiation headers must not describe the cached decoded response.
      const headers=new Headers(response.headers);for(const key of ['Content-Encoding','Content-Length','Transfer-Encoding','Vary'])headers.delete(key);
      if(file.source)headers.set('Content-Type','text/html; charset=utf-8');
      headers.set('Cache-Control','no-cache');
      await cache.put(url.href,new Response(bytes,{status:200,headers}));
    }
    await cache.put(META,new Response(JSON.stringify({createdAt:Date.now(),release:RELEASE}),{headers:{'Content-Type':'application/json'}}));
  }catch(error){await caches.delete(CACHE);throw error}
  // An update waits. No skipWaiting on install and no background financial replay.
})()));
self.addEventListener('activate',event=>event.waitUntil((async()=>{
  const entries=[];
  for(const key of await caches.keys())if(key.startsWith(PREFIX)){
    const meta=await (await caches.open(key)).match(META);if(meta){const value=await meta.json();entries.push({key,createdAt:value.createdAt})}
  }
  entries.sort((a,b)=>b.createdAt-a.createdAt);
  for(const entry of entries.slice(3))if(entry.key!==CACHE)await caches.delete(entry.key);
  await self.clients.claim();
})()));
self.addEventListener('message',event=>{
  if(event.data?.type==='GET_VERSION'){event.ports[0]?.postMessage({release:RELEASE});return}
  if(event.data?.type==='ACTIVATE'&&event.source){
    const source=new URL(event.source.url);if(source.origin===ROOT.origin&&source.pathname.startsWith(ROOT.pathname))event.waitUntil(self.skipWaiting());
  }
});
self.addEventListener('fetch',event=>{
  const request=event.request;const url=new URL(request.url);
  // Intentionally do not call respondWith for APIs, documents, credentials, public config, foreign hosts, queries, or Unity Build/StreamingAssets.
  if(request.method!=='GET'||url.origin!==ROOT.origin||financial(url))return;
  if(url.search){
    // Only this worker's exact public release tag is cacheable; arbitrary/token queries stay network-only.
    if(url.searchParams.size!==1||url.searchParams.get('__mangos_release')!==RELEASE)return;
    url.search='';if(!PUBLIC.has(url.href))return;
  }
  const navigation=request.mode==='navigate'&&(url.href===ROOT.href||url.href===INDEX);
  if(navigation){event.respondWith((async()=>{
    const cached=await (await caches.open(CACHE)).match(INDEX);
    // Keep the current shell and its IIFE consistent until an explicit update is applied.
    return cached??fetch(request);
  })());return}
  if(!PUBLIC.has(url.href))return;
  event.respondWith((async()=>await (await caches.open(CACHE)).match(url.href)??fetch(request))());
});
