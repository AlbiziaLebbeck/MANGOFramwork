import { createRequire } from 'node:module';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { resolve, sep } from 'node:path';
import { createHash } from 'node:crypto';
const root=resolve(import.meta.dirname,'../..');
const directory=resolve(process.argv[2]??'');
if(!directory.startsWith(resolve(root,'.artifacts/qr-real-readiness')+sep))throw new Error('Use an owned expired QR fixture directory');
const require=createRequire(resolve(root,'WebCommerce/package.json'));
const {chromium}=require('@playwright/test');
const fixture=JSON.parse(await readFile(resolve(directory,'fixtures.json'),'utf8'));
if(fixture.realBillerUsed||fixture.bankAccepted||fixture.fixtures.some(item=>!item.fixtureOnly||Date.parse(item.expiresAt)>Date.now()))throw new Error('Only expired document fixtures may be rendered');
const output=resolve(directory,'browser');await mkdir(output,{recursive:false});
const browser=await chromium.launch({headless:true});
const results=[];
try{
  for(const viewport of [{width:600,height:900},{width:390,height:844},{width:320,height:740}]){
    const page=await browser.newPage({viewport,deviceScaleFactor:1});
    await page.route('**/*',route=>route.abort());
    for(const item of fixture.fixtures){
      const bytes=await readFile(resolve(directory,item.file));
      if(createHash('sha256').update(bytes).digest('hex')!==item.sha256)throw new Error('Fixture PNG changed');
      await page.setContent('<html><body><div id="mango-ttb-root"><div class="commerce-dialog"><div class="payment-panel"><div class="qr-frame"><img alt="Expired document QR fixture; do not pay" width="280" height="280" src="data:image/png;base64,'+bytes.toString('base64')+'"></div></div></div></div></body></html>');
      await page.addStyleTag({path:resolve(root,'WebCommerce/src/styles.css')});
      const image=page.getByAltText('Expired document QR fixture; do not pay');
      await image.evaluate(img=>img.decode());
      const bounds=await image.boundingBox();if(!bounds||bounds.width<180||bounds.x<0||bounds.x+bounds.width>viewport.width)throw new Error('QR is too small or clipped');
      const name=`${viewport.width}-${item.file}`;
      const screenshot=await image.screenshot({path:resolve(output,name)});
      results.push({file:'browser/'+name,fixture:item.file,viewport,renderedWidth:bounds.width,renderedHeight:bounds.height,sha256:createHash('sha256').update(screenshot).digest('hex')});
    }
    await page.close();
  }
}finally{await browser.close()}
await writeFile(resolve(output,'result.json'),JSON.stringify({results,stylesSha256:createHash('sha256').update(await readFile(resolve(root,'WebCommerce/src/styles.css'))).digest('hex'),bankRequestMade:false,fixtureOnly:true},null,2));
console.log(JSON.stringify({screenshots:results.length,viewportWidths:[600,390,320],bankRequestMade:false}));
