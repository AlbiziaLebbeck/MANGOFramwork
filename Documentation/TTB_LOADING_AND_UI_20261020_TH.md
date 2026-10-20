# ผลตรวจ UI, Build และการโหลด TTB — 20 ตุลาคม 2026

## การแก้ UI และ Build

- เลื่อนตัวเลือกภาษาไปกลุ่มปุ่มด้านขวา ข้างปุ่มติดตั้ง/ดาวน์โหลด ใช้ grid ที่แบ่งซ้าย–ขวาเท่ากันเพื่อให้ Browse / Order history / My items อยู่กึ่งกลางหน้าจอบน desktop จริง แม้จำนวนปุ่มด้านขวาเปลี่ยนตามสถานะบัญชี
- มือถือคงเมนูหลักด้านล่างตามการออกแบบเดิม ปรับความกว้างภาษาและปุ่มให้ไม่ชนแบรนด์ที่จอแคบ มีภาษาในหน้าผู้ดูแลด้วย
- Unity มีเมนู/ปุ่ม `Build TTB UAT (Server + Client)` เตรียม React/HTML ล่าสุด สร้าง Server/Client และ ZIP โดยไม่ต้องเปิด terminal. Unity Editor โหลด C# ชุดใหม่แล้ว และพบ build สำเร็จจากปุ่มเวลา 11:00 น. ชุดนั้นเกิดก่อนแก้ header/การบีบอัดในรอบนี้ จึงต้อง Build ใหม่เพื่อรวมสองส่วนนี้

## ผลวัดการโหลดเว็บที่เผยแพร่

ตรวจ `https://plus.mangosgo.com/ttb-uat/` จาก PC วันที่ 20 ตุลาคม 2026:

| จุดตรวจ | ผล |
|---|---|
| `Client.data` | 24,506,247 bytes; application/octet-stream; ไม่มี Content-Encoding |
| `Client.wasm` | 60,248,818 bytes ก่อนบีบอัด; application/wasm; public ใช้ Brotli อยู่แล้ว |
| ชื่อไฟล์ Unity เดิม | `Client.data`, `Client.wasm`, `Client.framework.js`, `Client.loader.js` ซ้ำเมื่อ deploy ทับ slug/version เดิม จึงเสี่ยงใช้ไฟล์คนละรุ่นจาก HTTP cache |
| ดาวน์โหลด public `.data` | ภายใน 30 วินาทีได้ 2,023,126 bytes (~67,424 B/s) แล้วหยุดตามงบเวลาทดสอบ ไม่ใช่ full download |
| ดาวน์โหลด public `.wasm` | ภายใน 30 วินาทีได้ 676,458 wire bytes (~22,538 B/s) แล้วหยุดตามงบเวลาทดสอบ |
| อ่าน `.data` ผ่าน static Service ภายใน ntserver02 | ได้ครบ 24,506,247 bytes ใน 0.026515 วินาที |
| Chromium public cold load | หลัง 126.25 วินาทียังไม่มี unityInstance; หยุดทดสอบ ไม่รายงานเป็นเวลาโหลดสำเร็จ |

จึงมีหลักฐานว่าการส่งไฟล์ผ่านเส้นทาง public มาที่ PC ช้ากว่าการอ่านไฟล์ภายในมาก แต่ยังแยกไม่ได้ว่าเกิดที่ CDN/เส้นทางอินเทอร์เน็ต/เครือข่าย PC ช่วงใด ไม่พบ limit-rate ใน ingress ของ plus และ static server ใช้ file stream ปกติ. ไม่กล่าวโทษ CPU/ฐานข้อมูล/QR401 หรือ CORS โดยไม่มีหลักฐาน

## การแก้เฉพาะแพ็กเกจ TTB

`Tools/TTB/optimize-webgl.mjs` ทำงานอัตโนมัติก่อนแพ็ก PWA:

1. บีบอัด public data/wasm/framework ด้วย Brotli พร้อมตรวจว่าแตกไฟล์แล้วได้ bytes เดิมทุกตัว
2. เปลี่ยน URL ไฟล์ Build ทั้งสี่ให้มี SHA256 ของเนื้อหา เพื่อแยก cache ของเกมแต่ละรุ่น แม้ deploy ลง URL โลกเดิม
3. ตั้ง immutable เฉพาะข้อมูลฉาก `.data.br` ที่มีชื่อ hash ให้ Unity ใช้ cache ของข้อมูลเดิมได้ ไม่ใส่ API/บัญชี/QR/ใบเสร็จลง cache
4. บันทึก `Client/webgl-transfer.json` สำหรับตรวจขนาดในแต่ละ Build และลบเฉพาะ raw files ที่ entry ใหม่เลิกอ้างแล้ว

ทดสอบกับ Client ที่ Build สำเร็จจริงล่าสุด:

| ไฟล์ | ก่อนบีบอัด | ส่งแบบ Brotli |
|---|---:|---:|
| data | 24,506,247 | 13,165,823 |
| wasm | 60,248,818 | 12,697,458 |
| framework | 491,592 | 90,751 |
| loader (ไม่บีบอัดล่วงหน้า) | 26,976 | 26,976 |
| รวม | 85,273,633 | 25,981,008 |

ตัวเลขก่อนบีบอัดไม่ใช่ bandwidth ของเว็บเดิมทั้งหมด เพราะ CDN บีบ wasm อยู่แล้ว ข้อปรับปรุงใหม่ที่ยืนยันชัดคือ data ที่เล็กลงและไม่ต้องส่งซ้ำเมื่อใช้ cache ของ hash เดิม ไม่รับรองความเร็ว public ตามเวลาทดสอบ loopback และยังต้องตรวจหลังนำแพ็กเกจใหม่ขึ้น Portal

ตรวจ source ของ `mangos/metaverse-static-app-code` บน ntserver02 พบว่ารองรับ `.br` ด้วย Content-Encoding=br และ Content-Type ของนามสกุลก่อน .br อยู่แล้ว เช่น application/wasm. ไม่แก้ static server, shared ingress, Portal หรือ KKU. หากย้ายไป hosting อื่นต้องตั้ง headers ให้เทียบเท่า การเสิร์ฟไฟล์ .br เป็น application/octet-stream โดยไม่มี Content-Encoding จะใช้ไม่ได้

## ผล QA

Browser plugin not available; ใช้ Playwright/Chromium ตาม workflow ของโปรเจกต์. Flow: หน้า Local → ลงชื่อเข้าใช้บัญชีจำลอง → เปลี่ยนไทย/English → ตรวจตำแหน่งภาษา/เมนู → เปิดประวัติ. จอ 1920,1360,1024,800,390,320 ผ่านทั้งสองภาษา ไม่มี horizontal overflow หรือ page/console error และเมนู desktop อยู่กลางจอภายใน 1px

| ตรวจ | ผล |
|---|---|
| TypeScript รวม e2e | ผ่าน |
| Frontend unit | 33/33 |
| Browser: layout/SSO/ภาษา/ปุ่ม UAT ยังปิดตาม backend | 4/4 |
| PWA + compression/hash policy | 6/6 |
| Chromium โหลด native bytes จริงหลังบีบอัด | 1/1; Cold ~0.93s/Warm ~0.92s บน loopback เท่านั้น; warm ไม่มี data transfer; wasm ยัง transfer ได้ |
| PWA/CDN stale alias + HTML transformation | 1/1 |
| แพ็ก native/PWA ซ้ำ | release hash เดิม |

Native QA ไม่สร้างออเดอร์/จ่ายเงิน/เปิด Production. แก้ Local launcher ให้แยก `--migrate` กับ `--seed-demo` ตาม operator contract เดิม เพื่อให้เริ่ม API ทดสอบ UI ได้อีกครั้ง. API business logic ไม่เปลี่ยน

หลักฐานภาพ/timing อยู่ `C:/Users/PC/.codex/visualizations/2026/10/17/01a14abb-1984-7073-9312-25b2c6e8f444/ttb-build-help-20261020/` (`header-qa/`, `native-transfer/`, `pwa-final/`, `public-load-timings.json`). ไม่มี bank acceptance หรือผลความเร็ว public ของ Build ใหม่ในหลักฐานนี้

## พื้นที่ Build เก่า

ลบไฟล์ใน BuildWorkspace เก่า 73,750 ไฟล์ และ UatWorld เก่า 442 ไฟล์ รวม **17,365,738,718 bytes = 16.17 GiB**. ตรวจแล้วไม่มี build process อ้างอิงสองชุดนี้และไม่มี reparse point. เก็บ LocalGallery/LocalWorld ที่ยังมี preview/native process ใช้งาน และเก็บ Build สำเร็จล่าสุดนอก Builds/TTB ไว้

BuildWorkspace เหลือเพียง directory ว่างที่ process อื่นยังถือ handle จึงยังลบชื่อ directory ไม่ได้ ไม่มีไฟล์กินพื้นที่อยู่ ไม่ปิด process ของผู้ใช้เพื่อเอา directory ว่างออก. ถ้าเลือกวิธี Build แยกแบบ PowerShell ในอนาคต script จะสร้าง BuildWorkspace ใหม่ได้เอง แต่วิธีปุ่มใน Editor ใช้ Library หลักโดยตรง

รายการก่อน/หลังอยู่ `.artifacts/cleanup-20261020/deleted-builds.json` และ `result.json` (ไม่ commit generated artifacts)
