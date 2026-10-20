# บริษัทและสินค้าทดสอบ

ผู้ใช้อนุญาตให้ใช้ข้อมูลบริษัทจำลองก่อน และแก้ภายหลังได้ สินค้าจำลองต้องมีราคาตั้งแต่ 1 บาท บริษัทในเฟสนี้รับเงินรายเดียว และมีทั้งสินค้าจริงกับดิจิทัล

## เปลี่ยนข้อมูลบริษัท

เข้าระบบด้วยบัญชีที่มีสิทธิ์ admin → จัดการร้านค้า → ข้อมูลบริษัท แก้ชื่อบริษัท เลขผู้เสียภาษี 13 หลัก สาขา ที่อยู่ และข้อมูลติดต่อ แล้วบันทึก หากมีคนแก้ข้อมูลชุดเดียวกันก่อน ระบบปฏิเสธการบันทึกทับ ให้กดอัปเดตรายการแล้วตรวจใหม่

Local/UAT ใช้บริษัท MANGOs ทดสอบ จำกัด (ข้อมูลจำลอง), เลขผู้เสียภาษี `0000000000000`, สำนักงานใหญ่ (ทดสอบ), ที่อยู่จำลอง และ `test@example.invalid` เป็นค่าเริ่มต้น การแก้ไขเก็บใน PostgreSQL ตาราง `TestIssuerProfiles` และ audit ภายใต้สิทธิ์ admin+session+CSRF บริษัททดสอบไม่มีสถานะรับรองทางกฎหมาย

ข้อมูลใหม่มีผลกับเอกสารที่ยังไม่ออกเท่านั้น ชื่อ ที่อยู่ สาขา และข้อมูลติดต่อถูกบันทึกเป็น snapshot ในใบเสร็จ การดาวน์โหลดใบเสร็จเดิมหรือเอกสารยกเลิกที่อ้างใบเสร็จเดิมไม่เปลี่ยนตามชื่อบริษัทปัจจุบัน สาขา/ข้อมูลติดต่ออยู่ใน address ของ `receipt-v1` เพื่อคงรูปแบบและการตรวจ hash ของเอกสารเก่า

Production แสดงข้อมูลจากค่าตั้งระบบที่รับรองแล้วและแก้ผ่านหน้านี้ไม่ได้ ต้องเตรียมค่า `Receipts__IssuerName`, `Receipts__IssuerAddress`, `Receipts__TaxId`, `Receipts__Branch`, `Receipts__Contact` และ `Receipts__CompanyProfileApproved` ผ่านกระบวนการ release ก่อนเปิดรับเงินจริง เลขศูนย์ทั้งหมดใช้ผ่าน Production gate ไม่ได้ ตารางบริษัททดสอบไม่สามารถทับข้อมูลบริษัท Production

## สินค้าจำลองและราคา

| สินค้า | ประเภท | ราคา | modelKey |
|---|---|---:|---|
| Mango Star — Digital | สิทธิ์ดิจิทัลใช้ในเมตาเวิร์ส | 1 บาท | mango-star |
| Mango Sculpture | สินค้าจริง | 10 บาท | mango-sculpture |
| Orbit Lamp | สินค้าจริง | 99 บาท | orbit-lamp |

แก้ราคา รายละเอียด ตัวเลือก การเผยแพร่ และสต็อกได้ใน จัดการร้านค้า → สินค้า ใบเสนอราคา/ออเดอร์/ใบเสร็จที่มีอยู่ใช้ snapshot เดิม การเปลี่ยนสินค้าปัจจุบันไม่เปลี่ยนยอดเอกสารเก่า

เริ่ม Local ด้วย `Services/TtbCommerce/tools/local.ps1` ซึ่งใช้ฐาน development ที่แยกและ PaymentMode Mock เท่านั้น หากมี demo เดิมแล้ว คำสั่ง seed ไม่เขียนทับการแก้สินค้า สามารถตั้งราคาของสินค้าสาธิตสามชิ้นกลับเป็น 1/10/99 โดยสั่ง `Tools/TTB/set-local-demo-prices.ps1` อย่างชัดเจน เครื่องมือนี้รับเฉพาะ loopback Local+Mock และใช้ admin API ที่บันทึก audit ไม่เปลี่ยน stock/ออเดอร์เก่า

Seeder นี้ปฏิเสธ UAT/Production ไม่ใช้สินค้า/เครดิตจำลองทำให้ bank gate ผ่าน การตั้ง catalog ทดสอบของ UAT ต้องทำผ่านขั้นเตรียม catalog ที่แยกจาก simulator และต้องยังไม่เปิดรับเงินจริงจนข้อมูล TTB ครบ

## หลักฐานรอบ 19 ตุลาคม 2026

Backend CI `942a591eccee433c84859547e17ddef1`: 260/260 ไม่มี skip รวม concurrent profile update, admin/CSRF, immutable receipt, Production isolation และราคา seed ที่เริ่ม 1 บาท; target receipt/profile 13/13 ผ่านก่อน full CI

Private UAT release `20261019-p1-test-issuer`, archive SHA256 `fae3a9c3881ae9e825db5f0955c60f7db31767a8df5f9a5149940be9a92184a7`, reader 3 พร้อม capability `editable-test-issuer-v1`. Migration เพิ่มตารางอย่างเดียว ไม่มีการ seed mock order/credit หรือแก้ข้อมูลการเงิน UAT

Postcheck `.artifacts/issuer-qa/uat-postcheck-20261019.json`: UAT ทั้ง7 deployments และ KKU API พร้อม, bank/reports ปิด, financial8aggregatesและprofile rowsเป็น0, runtime trust ยังเป็น controller `/32` เดิมและไม่มี probe window. Backup role อ่านตารางใหม่ได้จริงตาม `has_table_privilege`; Linux worker สร้าง PDF จำลอง 136682 bytes ผ่าน และลบเฉพาะไฟล์ proof ใน `/tmp` แล้ว ซ้อม restore หลัง migration เพิ่มแล้ว: UAT44tables/21rows และLocal44tables/4067rows/127files รวมcompanyprofileที่มีข้อมูล1rowผ่าน ดูTTB_UAT_RECOVERY_EVIDENCE.md

Browser profile test ยืนยันแก้/บันทึก/reload/มือถือและราคา1/10/99 พร้อม no console errors. บริษัทจริง การรับชำระผ่านธนาคาร และการเปิด Production ยังต้องผ่าน gates ของแผน


Final QA: browser11/11, compiledgallery3/3, fullBaseWorld1/1, PWA1/1; Frontend CI f62e56ebba854719a2b69434e2dda7cb ผ่าน types/17unit/3PWA/build. หลักฐาน .artifacts/scene-qa/ui-company-final-20261019/ พร้อม hash ของ package และผลทดสอบครบ. BaseWorld ซื้อ physical99บาท+digital1บาทผ่าน UI ตั้งแต่ตะกร้าว่าง สร้างใบเสร็จPDFหนึ่งหน้า และใช้ไอเทมที่เพิ่งซื้อผ่าน server ticket/lease/logoutจริงใน Local. รูปPDFและข้อความที่ normalize NFKC/whitespaceตรวจแล้ว มีข้อมูลบริษัท/สาขา/contactและยอด100บาท. ไม่ได้จ่ายผ่านธนาคารจริง.



UAT catalog เตรียมจริงแล้วด้วยrelease `20261019-p2-uat-catalog` และfull268/268: สินค้า3ชิ้น99/10/1บาท, physical stock20ต่อชิ้น, digitalInventoryItem. ไม่มีการสร้างfinancial/effectใดและไม่เปิดsimulator. รายละเอียด/UID/liveproof `TTB_UAT_TEST_CATALOG.md`; ข้อมูลบริษัทUATยังใช้mockdefaultsและแก้ได้เมื่อบัญชีMANGOsของผู้ดูแลได้รับroleที่ยืนยันแล้ว ไม่ตั้งadminให้identityจำลอง
