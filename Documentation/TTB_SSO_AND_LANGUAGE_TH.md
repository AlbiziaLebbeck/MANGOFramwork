# บัญชี MANGOs และภาษาไทย–อังกฤษของระบบสินค้า

อัปเดต 20 ตุลาคม 2026

## การเชื่อมบัญชีอัตโนมัติ

รุ่นก่อนเรียก `GET /session` ตอนเปิดหน้าเว็บและเมื่อ Unity แจ้ง session changed คำขอนี้ตรวจเฉพาะ commerce session ที่ออกไว้แล้ว จึงได้ `authenticated=false` แม้ Launcher อ่าน MANGOs SSO cookie และล็อกอินสำเร็จ ผู้ซื้อจึงต้องกดเชื่อมบัญชีเพื่อให้ `POST /session` แลกเซสชันอีกครั้ง

รุ่นนี้ตรวจเซสชันเดิมก่อน ถ้ายังไม่มีจึงเรียก `POST /session` อัตโนมัติด้วย HttpOnly cookie ที่ browser ส่งตามปกติ Backend ตรวจบัญชีกับ MANGOs `/users/me` แล้วออก commerce session/CSRF ของตัวเอง ไม่มีการนำชื่อ/ID/role ที่ Unity หรือ browser กล่าวอ้างมาใช้แทนหลักฐานบัญชี ผู้ที่ลงชื่อเข้าใช้แล้วจึงเปิดประวัติและสินค้าของฉันได้โดยไม่ต้องกดเชื่อมซ้ำ

- startup, native host ready, login notification และ focus checks รวมคำขอที่กำลังทำอยู่ ไม่สร้าง exchange ซ้อน
- guest ที่ไม่มีบัญชียังคงเป็น guest ไม่ redirect เอง และจำกัด automatic exchange ไม่เกินครั้งต่อนาที เว้นแต่มี native login notification ใหม่
- Local/Mock ที่เปิด test identities ไม่เรียก external SSO ให้อัตโนมัติ
- account switch ล้างข้อมูลของบัญชีก่อนหน้าก่อนเชื่อมบัญชีใหม่ ข้อมูล order/cart/CSRF ยังตรวจสิทธิ์ผ่าน backend ตามเดิม
- explicit signout และ native disconnect ทำให้ผลคำขอเก่าที่ค้างอยู่กลับมาล็อกอินไม่ได้ focus timer ไม่ undo signout; native login ใหม่เริ่มตรวจใหม่ได้

Auth asset ที่ผู้ใช้แก้ยังตั้ง `authenticationMode: Auto` บน `https://plus.mangosgo.com/ttb-uat/` จึงเลือก FirstPartyCookieSso ตาม host เก็บชื่อ `TTB Payment`, public client ID และ redirect ใหม่ไว้ใน source/Build แล้ว ไม่สร้างหรือฝัง OAuth client secret การทำ third-party OAuth บน host อื่นยังต้องใช้ trusted code-exchange backend ตาม contract เดิม

## เปลี่ยนภาษา

เลือก `ไทย` หรือ `English` จากเมนูด้านบน เปลี่ยนข้อความหน้าร้าน สินค้า ตะกร้า checkout QR ประวัติ สินค้าของฉัน หน้าจัดการร้านค้า ข้อผิดพลาด ป้ายสถานะ และปุ่ม/คำอธิบายสำหรับ accessibility จำนวน 562 messages

การเปลี่ยนภาษาไม่ reload หน้า ไม่ออกจากบัญชี และไม่เปลี่ยนข้อมูลที่กรอก ตะกร้า quote, order ID, QR payload หรือสถานะจ่าย ค่าภาษาบันทึกไว้เฉพาะ `localStorage` key `mangos-ttb-language`; ตั้ง `html.lang` และรูปแบบวันที่/ตัวเลขตามภาษาที่เลือก โดยยังแสดงเวลา Bangkok และสกุล THB เดิม

ชื่อ/รายละเอียดสินค้าที่ร้านกรอก ข้อมูลบุคคล หลักฐานธนาคาร และ snapshot ใบเสร็จยังเป็นข้อมูลต้นฉบับ ไม่แปลหรือแก้เอกสารย้อนหลัง ใบเสร็จ PDF เดิมยังใช้รูปแบบภาษาไทย การเลือก English เปลี่ยน UI สำหรับดู/ดาวน์โหลดเอกสาร ไม่ได้เปลี่ยนเนื้อหาเอกสารที่ออกแล้ว Native launcher/HUD ใช้ระบบข้อความของ Unity เดิม

ตัวแปลอยู่ `WebCommerce/src/i18n.ts` และ dictionary `i18n-messages.ts` ใช้ interpolation ที่ React render เป็นข้อความ ไม่มี HTML replacement หรือการอ่าน cookie/token มาเก็บใน language preference

## ทดสอบและส่งมอบ

- Types และ unit tests 33/33 ผ่าน รวม cookie exchange, concurrent checks, stale response, account switch, signout, Local isolation, translation placeholders, storage blocked และ Bangkok date/THB formatting
- Browser language/SSO 2/2 ผ่าน: identity fixture ตรวจการส่ง HttpOnly cookie/POST ที่ไม่มี identity body และเปิด history/inventory โดยไม่กด login; อีก flow ใช้ Local API/Mock จริง ซื้อสินค้า 1 บาท สลับภาษาขณะกรอก checkout/ถือ quote/แสดง QR, จำลองจ่าย ดูประวัติ ดาวน์โหลด PDF และ reload preference พร้อมตรวจ portrait 390×844
- Browser regression เดิม 11/11 ผ่าน รวม keyboard focus, 320/390/844px, catalog, stock replay, fulfillment, finance, receipts, company editing, partial external refunds และ Void โดยใช้ Local/Mock เท่านั้น
- ชุด Server Linux/WebGL ใหม่: `Builds/TTB/Portal/a572d58dd99246d2bbf5dcdc349bcd6e/Release/Upload/` ใช้ slug/version เดียวกันทั้งคู่ตาม `TTB_DEPLOYMENT_PORTAL_TH.md` ยังไม่ได้อัปโหลดหรือเผยแพร่ทับ Portal
- Native private QA ของ Client/Server ชุดนี้ผ่าน 1/1 ใน 33.6s รวม native guest/server RPC, คลิกโมเดลจริง, หมุน/ซูม, สลับภาษา desktop/mobile 390×844 และคืน HUD/การเดิน ใช้ Client bytes จริงและ WS frames เดิมผ่าน SSH relay จึงยังไม่ใช่ผล public TLS ของ slug ที่ผู้ใช้เผยแพร่ ไม่มี financial mutation; anonymous session exchange แยกบันทึกจากคำขอการเงิน
- ปิด tunnel และลบ Job/Pod/NetworkPolicy ชั่วคราวแล้ว UAT 9 deployments พร้อม จำนวนข้อมูลบัญชีที่มีอยู่ก่อนเริ่ม QA (1 shopper/3 sessions) และกลุ่มการเงินทั้ง 16 กลุ่มไม่เปลี่ยน Portal/code, API, KKU และ shared service ตรง baseline ไม่มีการลบบัญชีจริงของผู้ใช้เพื่อให้ผลตรวจเป็นศูนย์

SSO fixture ไม่ใช่ผลตรวจบัญชี MANGOs จริงของผู้ใช้ หลังอัปโหลดชุดใหม่ต้องตรวจบัญชีจริงที่หน้า Launcher → ประวัติ → สินค้าของฉัน และ signout/account switch บน slug ที่เผยแพร่จริงอีกครั้ง การแก้ SSO/ภาษาไม่เปิด bank payment/callback หรือเปลี่ยน KKU/Production

Browser plugin สำหรับ workflow ทดสอบเฉพาะทางไม่อยู่ใน session จึงใช้ Playwright เดิมของโครงการ หลักฐานของ language flow อยู่ `C:/Users/PC/.codex/visualizations/2026/10/17/01a14abb-1984-7073-9312-25b2c6e8f444/ttb-session-i18n-20261020-final/` ผลรอบแรกที่ selector ไม่ตรงชื่อสินค้าและรอบที่คาดว่า paid order จะเปิดเองหลัง reload เก็บแยกไว้ และไม่ใช่ผลที่ใช้รับรองรอบสุดท้าย

Native proof อยู่ `C:/Users/PC/.codex/visualizations/2026/10/17/01a14abb-1984-7073-9312-25b2c6e8f444/ttb-native-session-i18n-20261020-final/` ภาพ English/Thai desktop/mobile เปิดตรวจแล้ว รันแรกสุ่มเกิดด้านข้างร้านจน lamp พ้นภาพและ color-based harness คลิก sculpture แทน จึงเพิ่ม bounded physical reposition พร้อมตรวจชื่อสินค้าจริง ไม่ใช้ product-open bridge หรือ browser identity ปลอมแก้ให้ผลผ่าน ผลรอบแรกยังคงเก็บไว้แยกกัน
