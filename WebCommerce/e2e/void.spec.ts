import { test, expect } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

test.use({viewport:{width:390,height:844}});
test('owned mobile history distinguishes confirmed void, downloads its PDF and removes digital use rights',async({page,playwright,request})=>{
  const config=await (await request.get('/ttb-commerce/v1/storefront/config')).json();
  expect(config.environment).toBe('Local');expect(config.paymentMode).toBe('Mock');expect(config.mockSimulation).toBe(true);
  const errors:string[]=[];let bankChecks=0;page.on('pageerror',error=>errors.push(error.message));page.on('request',req=>{if(req.url().endsWith('/verify'))bankChecks++});
  const subject=`qa-void-${Date.now()}`;
  await page.goto('/');await page.getByRole('button',{name:'เข้าสู่ระบบ',exact:true}).click();await page.getByRole('textbox',{name:'ชื่อบัญชีทดสอบ',exact:true}).fill(subject);await page.getByRole('button',{name:'เข้าสู่ระบบทดสอบ Local',exact:true}).click();
  await expect(page.getByRole('button',{name:'ออกจากระบบซื้อสินค้า',exact:true})).toBeVisible();
  await page.getByRole('button',{name:/Mango Star — Digital Mango Star/}).click();await page.getByRole('button',{name:'เพิ่มลงตะกร้า',exact:true}).click();await page.getByRole('button',{name:'ปิดหน้าต่าง',exact:true}).click();
  await page.getByRole('button',{name:'ตะกร้าสินค้า 1 ชิ้น',exact:true}).click();await page.getByRole('button',{name:'ดำเนินการสั่งซื้อ',exact:true}).click();await page.getByRole('textbox',{name:'ชื่อผู้ซื้อ',exact:true}).fill('ผู้ทดสอบเอกสารยกเลิก');await page.getByRole('button',{name:'ตรวจสอบยอดชำระ',exact:true}).click();
  await page.getByRole('button',{name:'สร้าง QR เพื่อชำระเงิน',exact:true}).click();await page.getByText('เครื่องมือทดสอบ Local',{exact:true}).click();await page.getByRole('button',{name:'จำลองการชำระ (ไม่มีเงินจริง)',exact:true}).click();
  await expect(page.getByRole('heading',{name:'ประวัติรายการ',exact:true})).toBeVisible();
  const orders=await (await page.request.get('/ttb-commerce/v1/orders')).json();const order=orders.items[0];expect(order.paymentState).toBe('Paid');
  await expect(page.getByRole('button',{name:'ดาวน์โหลดใบเสร็จ',exact:true})).toBeVisible({timeout:40000});
  const admin=await playwright.request.newContext({baseURL:'http://127.0.0.1:5176',extraHTTPHeaders:{Origin:'http://127.0.0.1:5176'}});
  try{
    const login=await admin.post('/ttb-commerce/v1/session/local',{data:{subject:'demo-admin'}});expect(login.ok()).toBe(true);const session=await login.json();expect(session.user.roles).toContain('admin');
    const refund=await admin.post(`/ttb-commerce/v1/admin/orders/${order.id}/void`,{headers:{'X-CSRF-Token':session.csrfToken},data:{operationId:randomUUID(),reason:'Local QA · customer requested cancellation',deliveryReviewed:true}});expect(refund.status()).toBe(202);
    const operation=await refund.json();await expect.poll(async()=>{const result=await admin.get(`/ttb-commerce/v1/admin/voids/${operation.id}`);return (await result.json()).state},{timeout:30000}).toBe('Confirmed');
    const refresh=page.getByRole('button',{name:'อัปเดตเอกสารและการส่งมอบ',exact:true});
    await expect(async()=>{await refresh.click();await expect(page.getByRole('button',{name:'ดาวน์โหลดเอกสารยกเลิก',exact:true})).toBeVisible()}).toPass({timeout:40000,intervals:[1000,2000]});
    await expect(page.getByText('ยกเลิกรับชำระ',{exact:true}).first()).toBeVisible();await expect(page.getByRole('button',{name:'ดาวน์โหลดใบเสร็จ',exact:true})).toBeVisible();
    expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
    const evidence=resolve('../.artifacts/void-qa');await mkdir(evidence,{recursive:true});await writeFile(resolve(evidence,'local-fixture.json'),JSON.stringify({mode:'Local+Mock',subject,orderId:order.id,voidId:operation.id}));
    await page.getByRole('heading',{name:'ประวัติรายการ',exact:true}).scrollIntoViewIfNeeded();await page.screenshot({path:resolve(evidence,'history-mobile-void.png'),fullPage:true});
    await page.locator('.document-item').filter({has:page.getByRole('button',{name:'ดาวน์โหลดเอกสารยกเลิก',exact:true})}).scrollIntoViewIfNeeded();await page.screenshot({path:resolve(evidence,'void-document-mobile.png'),fullPage:true});
    const pending=page.waitForEvent('download');await page.getByRole('button',{name:'ดาวน์โหลดเอกสารยกเลิก',exact:true}).click();const download=await pending;expect(download.suggestedFilename()).toMatch(/^LV.*\.pdf$/);expect((await readFile((await download.path())!)).subarray(0,5).toString()).toBe('%PDF-');
    await page.getByRole('button',{name:'ไปที่สินค้าของฉัน',exact:true}).click();await expect(page.getByText('สิทธิ์ถูกเพิกถอน',{exact:true})).toBeVisible();await expect(page.getByRole('button',{name:'ใช้ในเมตาเวิร์ส',exact:true})).toHaveCount(0);expect(bankChecks).toBe(0);expect(errors).toEqual([]);
  }finally{await admin.dispose()}
});
