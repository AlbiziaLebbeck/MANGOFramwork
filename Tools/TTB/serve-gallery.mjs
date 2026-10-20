import { createServer,request } from 'node:http';
import { createReadStream } from 'node:fs';
import { stat } from 'node:fs/promises';
import { extname,resolve,sep } from 'node:path';
const world=process.argv.slice(2).includes('--world');
const web=process.argv.slice(2).includes('--web');
if(world&&web)throw new Error('Choose a single local experience');
const port=web?5176:world?5188:5186;
const root=resolve(import.meta.dirname,web?'../../WebCommerce/dist':world?'../../Builds/TTB/LocalWorld/Client':'../../Builds/TTB/LocalGallery');
const types={'.html':'text/html; charset=utf-8','.js':'text/javascript; charset=utf-8','.css':'text/css; charset=utf-8','.wasm':'application/wasm','.json':'application/json','.webmanifest':'application/manifest+json','.svg':'image/svg+xml','.png':'image/png','.ttf':'font/ttf','.woff2':'font/woff2','.data':'application/octet-stream'};
createServer(async(req,res)=>{
  let pathname;try{pathname=decodeURIComponent(new URL(req.url,'http://127.0.0.1:5186').pathname)}catch{res.writeHead(400).end();return}
  if(pathname==='/ttb-commerce/v1'||pathname.startsWith('/ttb-commerce/v1/')){const upstream=request({hostname:'127.0.0.1',port:5286,path:req.url,method:req.method,headers:{...req.headers,host:'127.0.0.1:5286'}},response=>{res.writeHead(response.statusCode??502,response.headers);response.pipe(res)});upstream.on('error',()=>{if(!res.headersSent)res.writeHead(502);res.end()});req.pipe(upstream);return}
  let path=resolve(root,'.'+pathname);if(path!==root&&!path.startsWith(root+sep)){res.writeHead(403).end();return}
  try{let info=await stat(path);if(info.isDirectory()){path=resolve(path,'index.html');info=await stat(path)}res.writeHead(200,{'Content-Type':types[extname(path)]??'application/octet-stream','Content-Length':info.size,'Cache-Control':'no-store','X-Content-Type-Options':'nosniff'});if(req.method==='HEAD')res.end();else createReadStream(path).pipe(res)}catch{res.writeHead(404).end()}
}).listen(port,'127.0.0.1',()=>console.log(`TTB compiled ${web?'web overlay':world?'metaverse':'gallery'}: http://127.0.0.1:${port}/ (Local+Mock only)`));
