import { readFile,writeFile,readdir,mkdir,stat } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { resolve,relative,sep } from 'node:path';
import { pathToFileURL } from 'node:url';
const repository=resolve(import.meta.dirname,'../..');
const digest=bytes=>createHash('sha256').update(bytes).digest('hex');
const versionedAssets=new Set(['mango-ttb.js','mango-ttb.css','commerce/mango-ttb.js','commerce/mango-ttb.css','ttb-commerce-config.js','mango-ttb-pwa-boot.js','ttb-viewport.css','TemplateData/style.css']);
function versionEntryAssets(html,release){
  return html.replace(/\b(src|href)=(["'])([^"']+)\2/g,(attribute,name,quote,value)=>{
    const path=value.split(/[?#]/)[0];if(!versionedAssets.has(path.replace(/^\.\//,'')))return attribute;
    const url=new URL(value,'https://shell.invalid/');url.searchParams.delete('__mangos_release');
    if(release)url.searchParams.set('__mangos_release',release);
    return `${name}=${quote}${path}${url.search}${url.hash}${quote}`;
  });
}
export async function packagePwa(directory){
  const root=resolve(directory);if(!root.startsWith(repository+sep))throw new Error('PWA output must stay inside the project workspace');
  const indexPath=resolve(root,'index.html');let html=await readFile(indexPath,'utf8');
  html=html.replace(/<meta name="mango-ttb-release" content="[a-f0-9]{64}">/g,'');
  html=versionEntryAssets(html,null);
  if(!html.includes('rel="manifest"'))html=html.replace('</head>','<link rel="manifest" href="./manifest.webmanifest"><meta name="theme-color" content="#233d33"><script src="./mango-ttb-pwa-boot.js"></script></head>');
  if(!html.includes('window.MangoTtbLoadUnity?'))html=html.replace('document.body.appendChild(loader);','window.MangoTtbLoadUnity?window.MangoTtbLoadUnity(loader):document.body.appendChild(loader);');
  if(!html.includes('window.MangoTtbShowLoadError'))html=html.replace('console.error(error)})','if(window.MangoTtbShowLoadError)window.MangoTtbShowLoadError();console.error(error)})');
  const icon='<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512"><rect width="512" height="512" rx="100" fill="#233d33"/><path fill="#fff" d="M124 366V146h52l80 126 80-126h52v220h-54V240l-78 115-78-115v126z"/></svg>';
  await mkdir(resolve(root,'icons'),{recursive:true});await writeFile(resolve(root,'icons/mangos.svg'),icon);
  await writeFile(resolve(root,'mango-ttb-pwa-boot.js'),await readFile(resolve(repository,'WebCommerce/pwa/boot.js')));
  const manifest={id:'./',name:'MANGOs Gallery',short_name:'MANGOs',lang:'th',start_url:'./',scope:'./',display:'standalone',background_color:'#eef0ed',theme_color:'#233d33',icons:[{src:'./icons/mangos.svg',sizes:'any',type:'image/svg+xml',purpose:'any maskable'}]};
  await writeFile(resolve(root,'manifest.webmanifest'),JSON.stringify(manifest));
  const candidates=['index.html','manifest.webmanifest','mango-ttb-pwa-boot.js','icons/mangos.svg'];
  try{if((await stat(resolve(root,'TemplateData/style.css'))).isFile())candidates.push('TemplateData/style.css')}catch{}
  try{if((await stat(resolve(root,'ttb-viewport.css'))).isFile())candidates.push('ttb-viewport.css')}catch{}
  for(const base of ['','commerce/']){
    for(const file of ['mango-ttb.js','mango-ttb.css'])try{if((await stat(resolve(root,base+file))).isFile())candidates.push(base+file)}catch{}
    for(const folder of ['products','fonts'])try{for(const entry of await readdir(resolve(root,base+folder),{withFileTypes:true}))if(entry.isFile()&&/\.(png|webp|jpg|jpeg|svg|ttf|woff2)$/.test(entry.name))candidates.push(base+folder+'/'+entry.name)}catch{}
  }
  if(!candidates.some(file=>file.endsWith('mango-ttb.js')))throw new Error('Commerce IIFE missing from public shell');
  const initial=[];for(const path of candidates.sort()){const bytes=path==='index.html'?Buffer.from(html):await readFile(resolve(root,path));initial.push({path,size:bytes.length,sha256:digest(bytes)})}
  if(initial.reduce((sum,file)=>sum+file.size,0)>24*1024*1024)throw new Error('Public shell exceeds its 24 MiB cache limit');
  const source=await readFile(resolve(repository,'WebCommerce/pwa/service-worker.template.js'),'utf8');
  const release=digest(JSON.stringify(initial)+'\n'+source);html=versionEntryAssets(html,release).replace('</head>',`<meta name="mango-ttb-release" content="${release}"></head>`);await writeFile(indexPath,html);
  // HTML can be transformed by hosting analytics after upload. Cache only the exact built shell,
  // transported as JSON; never weaken integrity checks or trust arbitrary injected HTML.
  await writeFile(resolve(root,'mango-ttb-shell.json'),JSON.stringify({html}));
  const files=initial.map(file=>file.path==='index.html'?{...file,source:'mango-ttb-shell.json',size:Buffer.byteLength(html),sha256:digest(html)}:file);
  await writeFile(resolve(root,'mango-ttb-sw.js'),source.replace('__RELEASE__',JSON.stringify(release)).replace('__MANIFEST__',JSON.stringify(files)));
  await writeFile(resolve(root,'pwa-release.json'),JSON.stringify({release,files},null,2));
  return {release,files:files.length,bytes:files.reduce((sum,file)=>sum+file.size,0),root:relative(repository,root)};
}
if(process.argv[1]&&import.meta.url===pathToFileURL(resolve(process.argv[1])).href){const target=process.argv[2];if(!target)throw new Error('Provide a compiled public web output directory');console.log(JSON.stringify(await packagePwa(target)))}
