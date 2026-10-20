import { test,expect,type Page } from '@playwright/test';
import { mkdir,readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
const evidence=resolve('../.artifacts/ui-qa');
test.beforeAll(async({request})=>{await mkdir(evidence,{recursive:true});const response=await request.get('/ttb-commerce/v1/storefront/config');const config=await response.json();expect(config.environment).toBe('Local');expect(config.paymentMode).toBe('Mock');expect(config.mockSimulation).toBe(true)});
async function login(page:Page,suffix:string){await page.goto('/');await expect(page.getByRole('button',{name:/Orbit Lamp Orbit Lamp/})).toBeVisible();await page.getByRole('button',{name:'เข้าสู่ระบบ',exact:true}).click();await page.getByRole('textbox',{name:'ชื่อบัญชีทดสอบ',exact:true}).fill(`qa-${Date.now()}-${suffix}`);await page.getByRole('button',{name:'เข้าสู่ระบบทดสอบ Local',exact:true}).click();await expect(page.getByRole('button',{name:'ออกจากระบบซื้อสินค้า',exact:true})).toBeVisible()}
async function add(page:Page,name:RegExp,count:number){await page.getByRole('button',{name}).click();await page.getByRole('button',{name:'เพิ่มลงตะกร้า',exact:true}).click();await expect(page.getByRole('button',{name:`ตะกร้าสินค้า ${count} ชิ้น`,exact:true})).toBeVisible();await page.getByRole('button',{name:'ปิดหน้าต่าง',exact:true}).click()}
async function checkout(page:Page,count:number){await page.getByRole('button',{name:`ตะกร้าสินค้า ${count} ชิ้น`,exact:true}).click();await page.getByRole('button',{name:'ดำเนินการสั่งซื้อ',exact:true}).click();await page.getByRole('textbox',{name:'ชื่อผู้ซื้อ',exact:true}).fill('ผู้ทดสอบระบบ');const phone=page.getByRole('textbox',{name:'เบอร์โทรศัพท์',exact:true});if(await phone.count())await phone.fill('0812345678');await page.getByRole('button',{name:'ตรวจสอบยอดชำระ',exact:true}).click();await expect(page.getByRole('button',{name:'สร้าง QR เพื่อชำระเงิน',exact:true})).toBeEnabled()}
test('mixed purchase grants digital item, retains physical delivery, creates downloadable Thai receipt and protects another account',async({page})=>{
  const errors:string[]=[];page.on('pageerror',error=>errors.push(error.message));let bankChecks=0;page.on('request',request=>{if(request.url().endsWith('/verify'))bankChecks++});
  await login(page,'mixed');await add(page,/Orbit Lamp Orbit Lamp/,1);await add(page,/Mango Star — Digital Mango Star/,2);await checkout(page,2);
  await page.screenshot({path:resolve(evidence,'checkout-desktop.png')});await page.getByRole('button',{name:'สร้าง QR เพื่อชำระเงิน',exact:true}).click();await expect(page.getByRole('heading',{name:'ชำระด้วย QR',exact:true})).toBeVisible();
  await expect(page.getByRole('img',{name:'QR จำลองสำหรับทดสอบ ไม่มีการรับเงินจริง',exact:true})).toBeVisible();expect(bankChecks).toBe(0);
  await page.screenshot({path:resolve(evidence,'payment-desktop.png')});await page.getByText('เครื่องมือทดสอบ Local',{exact:true}).click();await page.getByRole('button',{name:'จำลองการชำระ (ไม่มีเงินจริง)',exact:true}).click();
  await expect(page.getByRole('heading',{name:'ประวัติรายการ',exact:true})).toBeVisible();await expect(page.getByText('รอจัดเตรียม',{exact:true})).toBeVisible({timeout:20000});
  const refresh=page.getByRole('button',{name:'อัปเดตเอกสารและการส่งมอบ',exact:true});const download=page.getByRole('button',{name:'ดาวน์โหลดใบเสร็จ',exact:true});
  await expect(async()=>{await refresh.click();await expect(download).toBeVisible()}).toPass({timeout:40000,intervals:[1000,2000]});
  await page.screenshot({path:resolve(evidence,'history-desktop.png')});const received=page.waitForEvent('download');await download.click();const file=await received;expect(file.suggestedFilename()).toMatch(/^LR.*\.pdf$/);const path=await file.path();expect((await readFile(path!)).subarray(0,5).toString()).toBe('%PDF-');
  await page.getByRole('button',{name:'สินค้าของฉัน',exact:true}).click();await expect(page.getByRole('heading',{name:'Mango Star — Digital',exact:true})).toBeVisible();await expect(page.getByText('พร้อมใช้งาน',{exact:true})).toBeVisible();
  await page.getByRole('button',{name:'ออกจากระบบซื้อสินค้า',exact:true}).click();await page.getByRole('button',{name:'เข้าสู่ระบบ',exact:true}).click();const other=`qa-${Date.now()}-other`;await page.getByRole('textbox',{name:'ชื่อบัญชีทดสอบ',exact:true}).fill(other);await page.getByRole('button',{name:'เข้าสู่ระบบทดสอบ Local',exact:true}).click();await expect(page.getByRole('button',{name:other,exact:true})).toBeVisible();await page.getByRole('button',{name:'ประวัติรายการ',exact:true}).click();await expect(page.getByText('ยังไม่มีประวัติรายการ',{exact:true})).toBeVisible();
  expect(bankChecks).toBe(0);expect(errors).toEqual([]);
});
test('mobile checkout retains QR on reload and sends no automatic Verify',async({page})=>{
  await page.setViewportSize({width:390,height:844});const errors:string[]=[];page.on('pageerror',error=>errors.push(error.message));let checks=0;page.on('request',request=>{if(request.url().endsWith('/verify'))checks++});
  await login(page,'mobile');await add(page,/Mango Sculpture Mango Sculpture/,1);await checkout(page,1);await page.getByRole('button',{name:'สร้าง QR เพื่อชำระเงิน',exact:true}).click();await expect(page.getByRole('img',{name:'QR จำลองสำหรับทดสอบ ไม่มีการรับเงินจริง',exact:true})).toBeVisible();
  const before=await page.locator('.payment-reference>strong').innerText();await page.reload();await expect(page.getByRole('heading',{name:'ชำระด้วย QR',exact:true})).toBeVisible();await expect(page.locator('.payment-reference>strong')).toHaveText(before);
  await page.screenshot({path:resolve(evidence,'qr-mobile.png'),fullPage:true});expect(await page.locator('.commerce-dialog').evaluate(element=>element.scrollWidth<=element.clientWidth+1)).toBe(true);expect(checks).toBe(0);expect(errors).toEqual([]);
  await page.getByRole('button',{name:'ยกเลิกคำสั่งซื้อ',exact:true}).click();await expect(page.getByRole('heading',{name:'ประวัติรายการ',exact:true})).toBeVisible();
});
test('a lost order response recovers the same operation instead of creating a second order',async({page})=>{
  await login(page,'recovery');await add(page,/Mango Sculpture Mango Sculpture/,1);await checkout(page,1);
  let loseFirst=true;const keys:string[]=[];
  await page.route('**/ttb-commerce/v1/orders',async route=>{if(route.request().method()!=='POST'){await route.continue();return}keys.push(route.request().headers()['idempotency-key']);if(loseFirst){loseFirst=false;const response=await route.fetch();expect(response.ok()).toBe(true);await route.abort('failed')}else await route.continue()});
  await page.getByRole('button',{name:'สร้าง QR เพื่อชำระเงิน',exact:true}).click();await expect(page.getByRole('alert')).toContainText('เชื่อมต่อไม่สำเร็จ');
  await page.getByRole('button',{name:'กู้คืนคำสั่งซื้อที่ส่งค้าง',exact:true}).click();await expect(page.getByRole('img',{name:'QR จำลองสำหรับทดสอบ ไม่มีการรับเงินจริง',exact:true})).toBeVisible();
  expect(keys).toHaveLength(2);expect(keys[0]).toBeTruthy();expect(keys[1]).toBe(keys[0]);const response=await page.request.get('/ttb-commerce/v1/orders');const history=await response.json();expect(history.items).toHaveLength(1);
  await page.getByRole('button',{name:'ยกเลิกคำสั่งซื้อ',exact:true}).click();await expect(page.getByRole('heading',{name:'ประวัติรายการ',exact:true})).toBeVisible();
});
