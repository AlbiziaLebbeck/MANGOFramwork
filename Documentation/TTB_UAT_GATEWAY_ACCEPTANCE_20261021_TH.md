# UAT หลัง TTB ลงทะเบียน public key — 21 ตุลาคม 2026

ผู้ใช้แจ้งว่า TTB ลงทะเบียน public key เดิมแล้ว และอนุญาตให้ดำเนินระบบ UAT ต่อ. ใช้ RSA2048 คู่เดิม SHA256 ของ DER-SPKI `4b65641eaf164b3b2c80cc9b12178d58ab88d1bc5dd310ec185d0f1a97f3291c`; ไม่สร้างหรือสลับ key.

## ผลสแกนและ callback จริง เวลา21:14:03น.

วันที่21ตุลาคม2026 TTBสแกนQRของออเดอร์ใหม่ `T20261021000002` ยอด1บาท. ฐานข้อมูลยืนยันcallbackจากbankIP `203.148.143.103`, Source=Callback, Service-Name=credit-notification, Outcome=Confirmed, ACK0000, DeliveryCount1. InstructionId `20261021211358646491811629421387865`; QRId `ZPYAE261021211900001`. รับcallback21:14:03.694881น. และorderเป็นPaid/Completed, มีallocation1รายการตรงยอด100สตางค์.

Fulfillmentดิจิทัล1ชิ้นDeliveredเวลา21:14:04.619148น., สิทธิ์Mango Starเป็นActive และใบเสร็จ `TR20261021000001` State=Ready. เป็นผลจากcallbackจริง ไม่ใช่VerifyหรือPaidที่แก้ด้วยมือ. หลักฐานคัดเฉพาะข้อมูลระบบโดยไม่รวมชื่อ/บัญชีผู้จ่ายอยู่ที่ `.artifacts/uat-callback-20261021/actual-bank-callback-T20261021000002.json`. ผลนี้ยืนยันUAT; การทดสอบใช้สินค้าในโลกผ่านUIและProductionเป็นคนละขั้นตอน. สถานะที่เขียนว่ารอcallbackในหัวข้อด้านล่างเป็นประวัติก่อนการสแกนครั้งนี้.

## อัปเดต callback หลังคำยืนยัน TTB เวลา11:02น.

คุณ Bird ยืนยันว่าระบบส่ง notification ไม่ใช้ username/password และผู้ใช้อนุญาตให้แก้ให้ตรง contract. เพิ่ม profile `BankSourceIp` ที่ต้องเปิดอย่างชัดเจนพร้อม `CallbackProfileApproved=true` และ IPธนาคารที่ถูกต้อง/ไม่ว่าง. Basic ยังรองรับสำหรับ profileที่เลือกBasic; Unavailable/None/ค่าที่ไม่รู้จักยังปิดรับ. แยกการตรวจ profileร่วมกันระหว่าง readiness กับ endpoint เพื่อไม่ให้เปิดปุ่มQRแต่รับcallbackไม่ได้.

โหมดใหม่ไม่ตรวจ Authorization แต่ยังบังคับ HTTPS/ingress allowlistเดิม, exact bank source IP ในapplication และ trusted proxyที่pinเฉพาะingresscontrollerจริง10.42.0.7/32. ไม่เชื่อ X-Real-IP และไม่ยอมให้ X-Forwarded-Forจากแหล่งอื่นหรือรายการซ้ายมือปลอมต้นทางธนาคาร. ไม่เปลี่ยนcallback acceptanceเป็นtrueก่อนมีหลักฐานจริง และไม่เปลี่ยนliveProduction.

Headers3รายการตามที่ TTB ส่งมารองรับแล้ว รวมRequest-Datetimeทศนิยม6หลัก; การตรวจbody/ยอด/refs/QRId, durableACK, deduplication และขั้นตอนจัดสรร/ส่งสินค้าคงเดิม. ข้อมูลส่งต่อใหม่อยู่ที่ [Provisioning/TTB_UAT_CALLBACK_HANDOFF_TH.md](Provisioning/TTB_UAT_CALLBACK_HANDOFF_TH.md). Helper `activate-bank-source-callback.py` เป็นdry-runโดยปริยาย; --applyต้องผ่านการตรวจcapabilityของAPI/worker, pinproxy/UIDและTLS/allowlistเดิมก่อนเปิดmodeและrestart. ไม่ใช้ผลunit testแทนการสแกน/callbackจากธนาคารจริง.

Backend CI ใหม่ run `451e694f87ab4c299893eae0d64c14f7` ผ่าน **283/283, ไม่มีskip, buildไม่มีwarning/error, inputFilesUnchanged=true**, รวมPostgreSQLและChromiumPDF. เทสต์ใหม่พิสูจน์ว่ารับheadersสามรายการโดยไม่มีcredentialsผ่านproxyจริงที่จำลองในdevelopmentได้, บันทึกครั้งเดียว/ตอบduplicateACKได้, IPปลอมและheaders/ยอด/refs/QRผิดไม่ส่งสินค้า, profileไม่อนุมัติและsource listว่าง/ผิดรูปแบบปิดรับ. Readinessโหมดใหม่ยังไม่อ้างactualcallback acceptance.

CIรอบก่อนหน้า `4a22b7dbdace44f58998d6fcc9265720` หยุดหลังพบassertionใหม่คาดUnpaidผิดจากพฤติกรรมเดิมที่เปลี่ยนยอดไม่ตรงเป็นReview; แก้เฉพาะassertionแล้วรันเต็มใหม่จนผ่าน. ไม่มีการนำartifactที่ทดสอบไม่ผ่านไปdeploy.

นำartifactที่ผ่าน283testsขึ้นrelease `20261021-bank-source-cb` แล้ว; archiveSHA256 `a9d213df07db42c48369aac12d0296416d569ecc8173def829803091f264e2f9`. API/commerceworker/receiptworker Ready1/1 ทุกตัว, migrationไม่มีschemaใหม่. Dry-runผ่านก่อนเปิดprofileจริง. Activationบันทึกที่ `/opt/ttb-commerce/uat/callback-source-profile/20261021T122528407956Z/state.json`; runtime API/worker เป็นBankSourceIpและusername/passwordว่างจริง.

หลังเปิดmode ตรวจpubliccallbackจากnon-bankและปลอมX-Forwarded-For/X-Real-IPรวมทั้งAPIloopbackปลอมต้นทางแล้ว403ครบ3กรณี. Publicshopper/CORS/ownership/protectedroutesผ่าน. UID/resourceVersionของprotectedresources12รายการ (รวมcallback/proxy/sharedingress/KKU/PortalUnityUAT/เกมNetworkPolicy) และbank/signingSecretเท่าเดิม. PublicconfigยังpaymentsReady=true, Bank, mockSimulation=false; callbackValidated/bankAcceptanceCompleteยังfalse.

ฐานข้อมูลหลังdeployยังOrders1, Paid0, CallbackReceipts0, BankTransactions0, Allocations0, Fulfillments0, Documents0เท่าก่อนdeploy. ไม่สร้างQRหรือส่งcallbackสำเร็จปลอมบนUAT; positive/duplicate/mismatch testsทำเฉพาะฐานdevelopmentชั่วคราว. ยังต้องให้TTBสแกนQRของorderใหม่เพื่อตรวจรับcallbackจริง. หลักฐานรอบนี้อยู่ใน `.artifacts/uat-callback-20261021/`.

## ผลจริงจากธนาคาร

Verify หนึ่งครั้งจาก API บน ntserver02 เวลา **10:05:45 น. ประเทศไทย** ได้ HTTP200, `ResCode=0002`, `Outcome=NotFound`, `authenticatedBankReply=true`. Request UID `4f4bc3c5-c49d-4ea0-97c7-d721031ff2c3`; QRId `ZPYAE261021102000001` เป็น diagnostic ที่ยังไม่จ่ายและหมดอายุแล้ว ห้ามนำกลับไปสแกน. ไม่มี order/payment/receipt/delivery ถูกสร้างจากคำสั่ง Verify นี้.

ผลนี้ยืนยัน gateway authentication และ response contract ของ Verify สำหรับ Merchant PYA / Biller075555900002101 / issuer jwt-key-pya. ไม่ถือเป็นหลักฐานจ่ายเงินหรือ callback acceptance.

## เปิดทดสอบ checkout โดยไม่ติดเงื่อนไข callback แรก

แยก `BankReadiness.MissingForCheckout()` ออกจาก `Missing()` ซึ่งยังเก็บเงื่อนไขตรวจรับธนาคารครบชุด. เฉพาะ Environment=Uat ที่ผู้ดูแลเปิด `Ttb.UatTestCheckoutEnabled=true` เท่านั้น จึงรอการยืนยัน callback แรกได้ระหว่างสร้างคำสั่งซื้อ/QR. Gateway authentication, signing key, QR profile, callback authentication profile และชื่อแสดงผู้รับยังต้องครบ. Production ไม่ใช้ข้อยกเว้นนี้ แม้ใส่ flag ผิดเป็นtrue.

`bank-acceptance.yaml` บันทึกหลักฐาน Verify และเปิด test checkout UAT. ชื่อแสดงผล `inam cafe` เป็นค่าชั่วคราวตามที่ผู้ใช้ระบุ ไม่ใช่การรับรองชื่อบัญชีโดย TTB. ไม่ตั้ง `CallbackSourceIpValidated=true` ล่วงหน้า. Public config แสดง `paymentsReady` สำหรับการสร้างคำสั่งซื้อ/QR และ `bankAcceptanceComplete`/`callbackValidated` แยกกัน. Metrics `ttb_bank_profile_ready` ยังคงใช้เงื่อนไขตรวจรับครบชุด.

Callback จริงบังคับ bank source IP ตามโหมดที่ธนาคารยืนยันด้านบน; ออเดอร์จะไม่เป็นPaid/ไม่ออกใบเสร็จหรือส่งสินค้าจนมีหลักฐานธนาคารที่ตรวจและจัดสรรสำเร็จ. ไม่เปิด simulator หรือ Local test identity ใน UAT.

## ประวัติข้อมูลส่งต่อ Basic ก่อนธนาคารยืนยันวิธีจริง

ผู้ใช้ยืนยันว่ายังไม่ได้ส่ง username/password ให้คุณ Bird. `recover-callback-handoff.ps1 -ExportPrivateText` กู้เฉพาะ credentials เดิมจาก Secret โดยไม่หมุนรหัส และสร้าง `.tools/bank-callback/Uat/52da601def104ab8838db6ba93ed2001/callback-uat-PRIVATE.txt` ในโฟลเดอร์ที่จำกัด ACL ให้ผู้ใช้ Windows ปัจจุบัน พร้อมสำเนา DPAPI. ไฟล์ส่วนตัวไม่เข้าGit และไม่มี credentials ในเอกสารนี้/แชต. ผู้ใช้ต้องส่งต่อเองผ่านช่องทางส่วนตัวที่ตกลงกับธนาคาร; งานนี้ไม่ได้ส่งข้อความภายนอก.

ข้อมูล Basicชุดก่อนหน้าเลิกใช้ในUATหลังเปิดBankSourceIp; ไม่ต้องส่งusername/passwordให้ธนาคาร. Helperกู้/แสดงรหัสตรวจeffectiveprofileก่อนและหยุดส่งออกBasicที่ไม่ใช้งานแล้ว. ให้สร้าง QR ของออเดอร์ใหม่จากหน้าเมตาเวิร์สและให้ TTB สแกนในวง UAT. ตรวจcallback/source/ACKพร้อมผลallocation/สต็อก/สิทธิ์/ใบเสร็จก่อนบันทึกacceptanceว่าครบ. แอปธนาคารทั่วไปยังไม่ใช่ช่องทางทดสอบUATตามที่TTBแจ้ง.

## Unity Server จาก Portal

Deployment `mangos/metaverse-server-ttb-uat` ถูกผูกกับ game identity UAT เดิมผ่าน Secret `mangos/ttb-portal-game-service` ซึ่งมีเฉพาะ game ID/key. ไม่คัดลอก bank/SQL credentials. NetworkPolicy ใหม่ `ttb-uat/ttb-api-portal-game` อนุญาตเฉพาะ pod ของเกมนี้ใน namespace mangos ไป API8080. Ingress เดิมและ policyเดิมไม่ถูกขยาย.

`configure-portal-game.py` รองรับ dry-run และ `--apply` พร้อมบันทึก before/after สำหรับ rollback บน server. หาก Portal redeploy แล้วล้าง env ให้รัน helper นี้หลังตรวจ slugอีกครั้ง. ทดสอบ HTTP จาก network ของ Unity pod จริงไป private commerce API ได้200. ยังต้องทดสอบนำสินค้าที่จ่ายสำเร็จแล้วมาใช้ในโลกหลังมีรายการจ่ายจริงใน UAT.

## สถานะการตรวจและ deploy

ชุดทดสอบเฉพาะ checkout/readiness/callback/gateway ผ่าน15/15 โดยใช้ฐานข้อมูล development แยกจาก UAT. Backend CI เต็มผ่าน **279/279, ไม่ข้ามรายการ, build ไม่มี warning/error**, รวม PostgreSQL migration/concurrency/callback และ Chromium Thai PDF. Run `34062da25dbe4b80964ff435c459cac3` เริ่ม10:20:28จบ10:34:56น.; `inputFilesUnchanged=true`, readerContract3. Linux self-contained API/ReceiptProbe จาก CI นี้ถูก deploy เป็น release `20261021-uat-checkout-v1`; archive SHA256 `8c57a24c1d457a6fd6e885d618c259ee42ec2315570f135d2baf77e23a415917`. API/commerce worker/receipt worker Ready1/1 ทุกตัว และ migration ยืนยันว่าไม่มี schema ใหม่ต้อง apply.

Public config หลัง deploy: Uat/Bank, paymentsReady=true, mockSimulation=false, uatTestCheckout=true; bankAcceptanceComplete=false และ callbackValidated=false. Public boundary verifier ผ่านจาก PC: config/catalog/session/CORS ใช้ได้, anonymous ownership401, untrusted Originถูกปฏิเสธ, admin/game/local-session/preview/callback ยังคง403. UID/resourceVersion ของprotected resource10รายการเดิมเท่าก่อนdeploy รวม ingressทั้ง3, API policyเดิม, proxy config, shared controller/Service และ KKU/ระบบร่วม. หลักฐานอยู่ใน `.artifacts/uat-20261021/`.

ผู้ใช้เข้าสู่ระบบ MANGOs ในแท็บทดสอบด้วยตนเอง. กดตะกร้าเดิม Mango Star2ชิ้น → ตรวจยอด2บาท → สร้างQR สำเร็จจริงที่ `https://plus.mangosgo.com/ttb-uat/`. Order `T20261021000001`, QRId `ZPYAE261021105100001`, Merchant PYA, Biller075555900002101, expected200สตางค์, expires21ตุลาคม10:51:00น. ชื่อแสดงผลinam cafe. ภาพหลักฐาน `live-order-qr.png` และPNGต้นฉบับ `T20261021000001-qr.png` อยู่ในโฟลเดอร์หลักฐานเดียวกัน; QRเป็นภาพการทดสอบครั้งนี้ เมื่อหมดอายุให้สร้างใหม่ในเวลานัดธนาคาร.

กดปุ่มตรวจสอบการชำระเงินบนหน้าเว็บหนึ่งครั้ง ได้ Verify HTTP200/0002/NotFound เวลา10:42:57น. Request UID `f48af372-4d1a-4fe5-b9b2-21adad7c34ea`. ฐานข้อมูลยืนยัน verificationCount1, orderยังUnpaid/Open, ไม่มี BankTransactions/Allocations/Fulfillments/Documents/CallbackReceipts. การสร้างและตรวจสอบQRผ่านแล้ว; การสแกน/รับcallback/จัดสรรเงิน/ส่งสินค้า/ออกใบเสร็จจริงยังต้องทดสอบร่วมกับTTB. ไม่กดจ่าย, ไม่เรียกVoid, ไม่สร้างPaidจำลอง, ไม่เปลี่ยนProduction.

## คอนโซลธนาคาร TEST และชื่อผู้รับเงิน

งานเพิ่มเติมวันที่21ตุลาคม2569: ใช้ชื่อผู้รับเงิน **บริษัท สร้างสรรค์ปัญญา จำกัด** ตามคำยืนยันและภาพจากผู้ใช้ เพิ่มคอนโซล UAT ส่วนตัวที่ Ref1=TEST แยกตารางจากออเดอร์/สต็อก/สิทธิ์/ใบเสร็จ รองรับ noti, Verify, Void ที่ผู้ดูแลยืนยัน และ Enquiry Void โดยห้ามส่ง Void ซ้ำหลัง send fence.

ชุดทดสอบ feature ผ่าน8/8; backend CI run `f146cf78d9bf4d31b7e3440b75226ed4` ผ่าน291/291 ไม่ข้ามรายการ, readerContract4 และ inputFilesUnchanged=true. สำรองข้อมูล UAT ก่อน migration สำเร็จใน job `ttb-uat-pre-banktests-20261021`. ติดตั้ง release `20261021-bank-test-console` แล้ว ตรวจหน้าเว็บจริง1440x1000/390x844, สร้างQR TEST1บาทและ VerifyHTTP200/0002/NotFound สำเร็จ ข้อมูลการเงินเดิมเท่าเดิมและ metadataป้องกัน14รายการไม่เปลี่ยน ดูผลทั้งหมดในคู่มือปัจจุบัน [ระบบ Payment และวิธีดู log](TTB_PAYMENT_IMPLEMENTATION_AND_TEST_CONSOLE_TH.md).

ข้อมูลชื่อชั่วคราวและการรอ callback ที่กล่าวในหัวข้อประวัติข้างต้นไม่ใช่ค่าที่ตั้งใจให้ใช้ต่อจากรอบนี้.