import { test,expect } from '@playwright/test';
import { mkdir,readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { nativePixel,launcherPixel,launcherClick } from './native-pixel';
import { clickRenderedProduct } from './rendered-product';
const prefix='/ttb-commerce/v1';
test('real FishNet owner consumes a purchased Local item, renews its lease and removes it on commerce logout',async({page})=>{
  const errors:string[]=[];const messages:string[]=[];page.on('pageerror',error=>errors.push(error.message));page.on('console',message=>{messages.push(message.text());if(message.type()==='error')errors.push(message.text().slice(0,800))});
  const evidence=resolve('../.artifacts/unity-qa');await mkdir(evidence,{recursive:true});
  const config=await (await page.request.get(prefix+'/storefront/config')).json();expect(config.environment).toBe('Local');expect(config.paymentMode).toBe('Mock');expect(config.gameUseReady).toBe(true);
  const suffix=Date.now().toString(36).slice(-6);const login=await page.request.post(prefix+'/session/local',{data:{subject:'native-qa-'+suffix},headers:{Origin:'http://127.0.0.1:5188'}});expect(login.ok()).toBe(true);
  expect((await (await page.request.get(prefix+'/inventory')).json()).items).toHaveLength(0);
  await page.goto('/');await expect(page.getByRole('button',{name:'เริ่มชมแกลเลอรี',exact:true})).toBeEnabled({timeout:120000});await page.getByRole('button',{name:'เริ่มชมแกลเลอรี',exact:true}).click();
  await expect.poll(()=>messages.some(message=>message.includes('MANGOS_CLIENT_STATE Started')),{timeout:30000}).toBe(true);
  // Transport Started precedes Launcher.Init. Observe the rendered guest label before clicking its native button.
  await expect.poll(async()=>{const glyph=await launcherPixel(page,620,456),gap=await launcherPixel(page,640,456);return glyph.every(value=>value>245)&&gap[1]<225},{timeout:15000}).toBe(true);
  await page.screenshot({path:resolve(evidence,'world-login-clear-chrome.png')});
  await launcherClick(page,640,456);
  // Wait for the native input underline to render before focusing it. Legacy Unity UI does not expose DOM nodes.
  // AuthenticationCanvas has its own layout group, so use its observed 1280x720 viewport positions.
  await expect.poll(async()=>(await nativePixel(page,500,467)).every(value=>value<150),{timeout:15000}).toBe(true);
  await page.mouse.click(640,446,{delay:120});await page.keyboard.type('TTBQA'+suffix,{delay:90});await page.keyboard.press('Tab');
  // The native validator uses scaled game time, so observe its enabled-button fill rather than a wall-clock sleep.
  await expect.poll(async()=>(await nativePixel(page,520,510)).every(value=>value>225),{timeout:15000}).toBe(true);await page.mouse.click(640,523,{delay:120});
  await expect(page.locator('#unity-canvas')).toHaveAttribute('data-commerce-ready','true',{timeout:60000});
  await expect(page.locator('#unity-canvas')).toHaveAttribute('data-game-ready','true',{timeout:60000});
  await page.screenshot({path:resolve(evidence,'world-arrival.png')});
  // Exercise the product raycast in the actual multiplayer scene, as well as the standalone gallery.
  await clickRenderedProduct(page,'lamp');await expect(page.getByRole('heading',{name:'Orbit Lamp',exact:true})).toBeVisible();
  await expect(page.getByRole('button',{name:'ซูมเข้า',exact:true})).toBeEnabled();await page.screenshot({path:resolve(evidence,'world-product-native.png')});
  await page.getByRole('button',{name:'เพิ่มลงตะกร้า',exact:true}).click();await expect(page.getByRole('button',{name:'ตะกร้าสินค้า 1 ชิ้น',exact:true})).toBeVisible();await page.getByRole('button',{name:'ปิดหน้าต่าง',exact:true}).click();
  await clickRenderedProduct(page,'star');await expect(page.getByRole('heading',{name:'Mango Star — Digital',exact:true})).toBeVisible();
  await page.getByRole('button',{name:'เพิ่มลงตะกร้า',exact:true}).click();await expect(page.getByRole('button',{name:'ตะกร้าสินค้า 2 ชิ้น',exact:true})).toBeVisible();await page.getByRole('button',{name:'ปิดหน้าต่าง',exact:true}).click();
  await page.getByRole('button',{name:'ตะกร้าสินค้า 2 ชิ้น',exact:true}).click();await page.getByRole('button',{name:'ดำเนินการสั่งซื้อ',exact:true}).click();
  await page.getByRole('textbox',{name:'ชื่อผู้ซื้อ',exact:true}).fill('ผู้ทดสอบ BaseWorld');await page.getByRole('textbox',{name:'เบอร์โทรศัพท์',exact:true}).fill('0812345678');
  await page.getByRole('button',{name:'ตรวจสอบยอดชำระ',exact:true}).click();await expect(page.getByRole('button',{name:'สร้าง QR เพื่อชำระเงิน',exact:true})).toBeEnabled();await page.getByRole('button',{name:'สร้าง QR เพื่อชำระเงิน',exact:true}).click();
  await expect(page.getByRole('img',{name:'QR จำลองสำหรับทดสอบ ไม่มีการรับเงินจริง',exact:true})).toBeVisible();await page.screenshot({path:resolve(evidence,'world-mixed-qr.png')});
  await page.getByText('เครื่องมือทดสอบ Local',{exact:true}).click();await page.getByRole('button',{name:'จำลองการชำระ (ไม่มีเงินจริง)',exact:true}).click();
  await expect(page.getByRole('heading',{name:'ประวัติรายการ',exact:true})).toBeVisible();await expect(page.getByText('รอจัดเตรียม',{exact:true})).toBeVisible({timeout:20000});
  const refresh=page.getByRole('button',{name:'อัปเดตเอกสารและการส่งมอบ',exact:true}),download=page.getByRole('button',{name:'ดาวน์โหลดใบเสร็จ',exact:true});
  await expect(async()=>{await refresh.click();await expect(download).toBeVisible()}).toPass({timeout:40000,intervals:[1000,2000]});await page.screenshot({path:resolve(evidence,'world-mixed-history.png')});
  const receive=page.waitForEvent('download');await download.click();const receipt=await receive;const receiptPath=resolve(evidence,'world-mixed-receipt.pdf');await receipt.saveAs(receiptPath);expect((await readFile(receiptPath)).subarray(0,5).toString()).toBe('%PDF-');
  await expect(page.getByRole('heading',{name:'ประวัติรายการ',exact:true})).toBeInViewport();
  await expect(page.locator('.history-row.selected')).toBeInViewport();
  await expect(page.getByRole('button',{name:'ปิดหน้าต่าง',exact:true})).toBeInViewport();
  await expect(page.locator('.commerce-progress')).toHaveCount(0);
  await page.screenshot({path:resolve(evidence,'world-mixed-history.png')});
  await page.getByRole('button',{name:'ปิดหน้าต่าง',exact:true}).click();
  // Equip the digital item bought through the visible checkout above. No pre-granted inventory fixture is used.
  await expect.poll(async()=>(await (await page.request.get(prefix+'/inventory')).json()).items.length,{timeout:30000}).toBe(1);
  await page.getByRole('button',{name:'สินค้าของฉัน',exact:true}).click();const use=page.getByRole('button',{name:'ใช้ในเมตาเวิร์ส',exact:true});await expect(use).toBeEnabled({timeout:30000});await use.click();
  await expect(page.getByRole('status').filter({hasText:'ใช้สินค้าในเมตาเวิร์สแล้ว'})).toBeVisible({timeout:15000});await page.getByRole('button',{name:'ปิดหน้าต่าง',exact:true}).click();
  await page.screenshot({path:resolve(evidence,'world-equipped.png')});
  // Cross the 15-second initial lease: equipment must survive a real renewal.
  await page.waitForTimeout(17000);await expect(page.getByRole('status').filter({hasText:'หยุดใช้สินค้าแล้ว'})).toBeHidden();
  await page.screenshot({path:resolve(evidence,'world-lease-renewed.png')});
  await page.getByRole('button',{name:'ออกจากระบบซื้อสินค้า',exact:true}).click();
  await expect(page.getByRole('button',{name:'เข้าสู่ระบบ',exact:true})).toBeVisible();await expect(page.getByRole('status').filter({hasText:'หยุดใช้สินค้าแล้ว'})).toBeVisible({timeout:15000});await page.screenshot({path:resolve(evidence,'world-logout-removed.png')});expect(errors).toEqual([]);
});
