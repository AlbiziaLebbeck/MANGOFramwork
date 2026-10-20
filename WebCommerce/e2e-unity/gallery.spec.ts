import { test,expect,type Page } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
const evidence=resolve('../.artifacts/unity-qa');
async function enter(page:Page){await page.goto('/');await expect(page.getByRole('button',{name:'เริ่มชมแกลเลอรี',exact:true})).toBeEnabled({timeout:120000});await page.getByRole('button',{name:'เริ่มชมแกลเลอรี',exact:true}).click();await expect(page.locator('#unity-canvas')).toHaveAttribute('data-commerce-ready','true',{timeout:20000})}
function nativeErrors(page:Page){const errors=new Set<string>();page.on('pageerror',error=>errors.add(error.message));page.on('console',message=>{if(message.type()==='error')errors.add(message.text().slice(0,800))});return errors}
test.beforeAll(async({request})=>{await mkdir(evidence,{recursive:true});const config=await (await request.get('/ttb-commerce/v1/storefront/config')).json();expect(config.environment).toBe('Local');expect(config.paymentMode).toBe('Mock')});
test('compiled Unity model click, native orbit/zoom/reset, HTML focus and safe inventory availability',async({page})=>{
  const errors=nativeErrors(page);await enter(page);
  await page.screenshot({path:resolve(evidence,'gallery-desktop.png')});
  await page.mouse.click(366,332);await expect(page.getByRole('heading',{name:'Orbit Lamp',exact:true})).toBeVisible();
  const rotate=page.getByRole('button',{name:/หมุนโมเดล|หมุนอัตโนมัติ/});await expect(rotate).toBeEnabled();
  // The native framebuffer, rather than a fake bridge, must visibly change in the preview area.
  const box=await page.locator('.preview-pane').boundingBox();expect(box).not.toBeNull();
  const clip={x:box!.x+box!.width*.2,y:box!.y+box!.height*.2,width:box!.width*.6,height:box!.height*.5};
  const before=await page.screenshot({clip});await rotate.click();
  await expect.poll(async()=>!(await page.screenshot({clip})).equals(before)).toBe(true);
  await page.getByRole('button',{name:'ซูมเข้า',exact:true}).click();await page.screenshot({path:resolve(evidence,'product-native-desktop.png')});
  await page.getByRole('button',{name:'คืนมุมมองเริ่มต้น',exact:true}).click();await page.keyboard.press('Escape');await expect(page.getByRole('heading',{name:'Orbit Lamp',exact:true})).toBeHidden();
  await page.mouse.click(918,340);await expect(page.getByRole('heading',{name:'Mango Star — Digital',exact:true})).toBeVisible();await expect(rotate).toBeEnabled();
  await page.getByRole('button',{name:'ปิดหน้าต่าง',exact:true}).click();expect([...errors]).toEqual([]);
});
test('phone tap opens native preview with bounded touch zoom and no horizontal overflow',async({browser})=>{
  const context=await browser.newContext({viewport:{width:390,height:844},isMobile:true,hasTouch:true,deviceScaleFactor:1});const page=await context.newPage();const errors=nativeErrors(page);
  try{await enter(page);await page.touchscreen.tap(195,390);await expect(page.getByRole('heading',{name:'Mango Sculpture',exact:true})).toBeVisible();await expect(page.getByRole('button',{name:'ซูมเข้า',exact:true})).toBeEnabled();
    const box=await page.locator('.preview-pane').boundingBox();expect(box).not.toBeNull();const clip={x:box!.x+box!.width*.2,y:box!.y+box!.height*.2,width:box!.width*.6,height:box!.height*.5};const before=await page.screenshot({clip});
    const cdp=await context.newCDPSession(page);const x=box!.x+box!.width/2,y=box!.y+box!.height/2;
    await cdp.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x:x-30,y,id:1},{x:x+30,y,id:2}]});
    await cdp.send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{x:x-65,y,id:1},{x:x+65,y,id:2}]});await cdp.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});
    await expect.poll(async()=>!(await page.screenshot({clip})).equals(before)).toBe(true);await page.screenshot({path:resolve(evidence,'product-native-mobile-zoomed.png')});await page.getByRole('button',{name:'คืนมุมมองเริ่มต้น',exact:true}).click();await page.screenshot({path:resolve(evidence,'product-native-mobile.png')});expect(await page.locator('.commerce-dialog').evaluate(element=>element.scrollWidth<=element.clientWidth+1)).toBe(true);expect([...errors]).toEqual([]);
  }finally{await context.close()}
});

test('a missing native model shows the correct product image and retry recovers from refreshed catalogue metadata',async({page})=>{
  const errors=nativeErrors(page);let injected=false;let financialWrites=0;
  page.on('request',request=>{if(request.method()!=='GET'&&/\/(cart|quotes|orders|mock|verify|enquiry)(\/|$)/.test(new URL(request.url()).pathname))financialWrites++});
  // Read-only catalogue transport fault. Native model lookup/error handling remains the real compiled Unity runtime.
  await page.route('**/ttb-commerce/v1/catalog/by-model/**',async route=>{
    const response=await route.fetch();const product=await response.json();
    if(!injected){injected=true;product.thumbnailUrl='/commerce/products/orbit-lamp.png';product.preview.modelKey='qa_model_not_installed'}
    await route.fulfill({response,json:product});
  });
  await enter(page);await page.mouse.click(366,332);
  await expect(page.getByRole('heading',{name:'Orbit Lamp',exact:true})).toBeVisible();
  await expect(page.getByText('เปิดโมเดล 3D ไม่สำเร็จ',{exact:true})).toBeVisible();
  await expect(page.locator('.preview-pane').getByRole('img',{name:'Orbit Lamp',exact:true})).toBeVisible();
  await expect(page.getByText('กำลังเปิดโมเดล 3D…',{exact:true})).toBeHidden();
  await expect(page.getByRole('button',{name:'ซูมเข้า',exact:true})).toBeDisabled();
  await page.getByRole('button',{name:'เพิ่มจำนวน',exact:true}).click();
  await expect(page.getByLabel('จำนวน',{exact:true})).toHaveText('2');
  await page.screenshot({path:resolve(evidence,'product-native-missing-model.png')});
  await page.getByRole('button',{name:'ลองเปิดโมเดลอีกครั้ง',exact:true}).click();
  await expect(page.getByRole('button',{name:'ซูมเข้า',exact:true})).toBeEnabled();
  await expect(page.getByText('เปิดโมเดล 3D ไม่สำเร็จ',{exact:true})).toBeHidden();
  await expect(page.getByLabel('จำนวน',{exact:true})).toHaveText('2');
  await page.screenshot({path:resolve(evidence,'product-native-model-recovered.png')});
  await page.keyboard.press('Escape');expect(financialWrites).toBe(0);expect([...errors]).toEqual([]);
});
