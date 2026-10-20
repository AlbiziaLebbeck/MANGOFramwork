import { createServer,request } from 'node:http';
import { createReadStream } from 'node:fs';
import { cp,mkdir,readFile,writeFile,stat } from 'node:fs/promises';
import { resolve,sep,extname } from 'node:path';
import { packagePwa } from '../package-pwa.mjs';
import { gzipSync } from 'node:zlib';
const repository=resolve(import.meta.dirname,'../../..');
const fixtures=resolve(repository,'.artifacts/pwa-browser-fixtures');
if(!fixtures.startsWith(repository+sep+'.artifacts'+sep))throw new Error('Invalid fixture root');
await mkdir(fixtures,{recursive:true});
for(const version of ['v1','v2']){
  const root=resolve(fixtures,version);await cp(resolve(repository,'WebCommerce/dist'),root,{recursive:true,force:true});
  if(version==='v2')await writeFile(resolve(root,'mango-ttb.js'),await readFile(resolve(root,'mango-ttb.js'),'utf8')+'\n/* PWA browser fixture version 2 */\n');
  await packagePwa(root);
}
let root=resolve(fixtures,'v1');
const types={'.html':'text/html; charset=utf-8','.js':'text/javascript; charset=utf-8','.css':'text/css; charset=utf-8','.json':'application/json','.webmanifest':'application/manifest+json','.svg':'image/svg+xml','.png':'image/png','.ttf':'font/ttf'};
createServer(async(req,res)=>{
  const pathname=new URL(req.url,'http://127.0.0.1:5176').pathname;
  if(pathname==='/__qa/release2'&&req.method==='POST'){root=resolve(fixtures,'v2');res.writeHead(204).end();return}
  if(pathname.startsWith('/ttb-commerce/v1/')){const upstream=request({hostname:'127.0.0.1',port:5286,path:req.url,method:req.method,headers:{...req.headers,host:'127.0.0.1:5286'}},response=>{res.writeHead(response.statusCode??502,response.headers);response.pipe(res)});upstream.on('error',()=>{if(!res.headersSent)res.writeHead(502);res.end()});req.pipe(upstream);return}
  let path;try{path=resolve(root,'.'+decodeURIComponent(pathname));if(path!==root&&!path.startsWith(root+sep)){res.writeHead(403).end();return}if((await stat(path)).isDirectory())path=resolve(path,'index.html');const info=await stat(path);const headers={'Content-Type':types[extname(path)]??'application/octet-stream','Cache-Control':'no-store'};
    if(/\.(?:html|js|css|webmanifest|svg|json)$/.test(path)){const compressed=gzipSync(await readFile(path));res.writeHead(200,{...headers,'Content-Encoding':'gzip','Vary':'Accept-Encoding','Content-Length':compressed.length});res.end(compressed)}else{res.writeHead(200,{...headers,'Content-Length':info.size});createReadStream(path).pipe(res)}
  }catch{res.writeHead(404).end()}
}).listen(5176,'127.0.0.1',()=>console.log('PWA isolated QA fixtures ready on loopback 5176'));
