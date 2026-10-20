# การปรับระบบจากคำตอบ TTB — 20 ตุลาคม 2026

ติดตั้ง/ทดสอบเฉพาะ UAT บน ntserver02 ไม่แก้ KKU/Deployment PortalหรือเปิดProduction. Production exampleเป็นข้อมูลเตรียมตรวจรับ

Merchant`PYA`, Biller`075555900002101`, issuer`jwt-key-pya`, header`alg=RS256,type=JWT`, token120วินาที. UATเฉพาะวงธนาคาร ให้คุณbirdสแกน; SFTP/PGPยังรอธนาคาร

## Callback handoff

Basic authenticationของUATเตรียมในSecret`ttb-uat/ttb-bank`. สำเนาPC `.tools/bank-callback/Uat/52da601def104ab8838db6ba93ed2001/handoff.dpapi`เข้ารหัสสำหรับบัญชีWindowsนี้ ไม่อยู่Git **ยังไม่ได้ส่งcredentialsให้ธนาคาร**

ผู้ใช้เปิดดูใน terminalส่วนตัว:

```powershell
./Infrastructure/TTB/uat/show-callback-credentials.ps1 -RunId 52da601def104ab8838db6ba93ed2001
```

ส่งให้ผู้ประสานธนาคารผ่านช่องทางปลอดภัยที่ตกลงกัน พร้อมURL UAT ไม่คัดลอกลงGit/issue/เอกสารสาธารณะ. ถ้าธนาคารยังไม่ได้รับรหัส ยังตรวจcallbackร่วมกันไม่ครบ

callbackเป็นExact path, TLS certificateเดิม, bank NAT allowlist6IPตามสเปก ตรวจBasic+sourceซ้ำในแอป ไม่เชื่อX-Forwarded-Forจากผู้เรียกทั่วไป. `CallbackSourceIpValidated`คงfalseจนเห็นผลTTBจริง callbackแรกผ่านIP+authเก็บหลักฐานได้แต่ไม่ปลดcheckoutflagเอง

## Public key

UAT public keyเดิม `Provisioning/ttb-uat-jwt-5183ee1873474f4ab9aff6f1b530d588-public.pem`, DER-SPKI SHA256 `4b65641eaf164b3b2c80cc9b12178d58ab88d1bc5dd310ec185d0f1a97f3291c`. ไม่ใช้ sample key/tokenในExcel ไม่สร้างกุญแจใหม่โดยไม่จำเป็น ผลVerifyตรวจว่าธนาคารรับkey/profileแล้วหรือยัง

## ผลตรวจรับรอบนี้

Verifyจริง1ครั้งจากAPIpodบนntserver02 วันที่20Oct2026 เวลา10:01:36ไทย ถึงgatewayUATและได้ **HTTP401**. Request UID `63ef695d-d960-44c9-985c-3b2a82a1c64a`, QRId `ZPYAE261020101600001` (รูปหมดอายุ10:16น. อย่านำกลับไปจ่าย). ใช้Merchant`PYA`, issuer`jwt-key-pya`, RS256/typeJWT และ public-key fingerprintข้างต้น ไม่ใส่sender_nameเพราะQR-specific contractไม่กำหนดและยังไม่มีค่าของบริษัท

401ยืนยันว่าgatewayตอบ แต่ **ยังไม่ผ่านauthentication** ไม่ยืนยันว่าkeyregisteredหรือAPIcontractสำเร็จ และไม่พิสูจน์ว่าเหตุเกิดจากkeyอย่างเดียว. ให้ธนาคารใช้RequestUIDตรวจ issuer/key registration/เงื่อนไขgateway เช่นsender_nameถ้าจำเป็น ไม่เดาค่า ไม่ retry/polling และไม่ยิงVoidจริง. `GatewayAuthenticationValidated=false`กั้นcheckoutไว้จนตรวจผ่าน

callback ingressUATติดตั้งแล้ว: `/ttb/v1/credit-notification`แบบExact, bank6IP, TLS+Basic. ตรวจจากIPoperatorได้403 ขณะที่store/config200และmetrics/readiness404ตามเดิม. Sourceacceptanceยังfalse ชื่อผู้รับเงินจริงยังไม่ยืนยัน และยังไม่ส่งรหัสBasicให้ธนาคาร จึงไม่อ้างว่าพร้อมรับชำระครบวงจร

QRdiagnosticPYA1บาทสร้างและบันทึกPNGได้จริง มีexpiryชัดเจน ไม่สร้างorders/attempts/financialrecords. ให้สร้างใหม่ตอนถึงนัดด้วย `Infrastructure/TTB/uat/bank-diagnostics.ps1 -Mode Qr`

Browserตรวจหน้าUATด้วยdisplayfixtureบนLocalAPI: ไทยdesktop1360×900/อังกฤษmobile390×844, ตรวจคำอธิบายวงธนาคารและปุ่มQRที่ยังdisabled. Types/unit33/33 และSSO/Localpurchase2/2ผ่าน; ไม่ได้อ้างว่าหน้าร้านที่ผู้ใช้เผยแพร่ในPortalอัปเดตอัตโนมัติ

คู่Portalbuildใหม่ `720b6fb4df314d22a0fc0258f0fb3824` ผ่านLinuxServer/WebGLและprotectedroot5ไฟล์เดิม อยู่ `Builds/TTB/Portal/720b6fb4df314d22a0fc0258f0fb3824/Release/Upload/`; ยังไม่อัปโหลดPortal. ServerSHA256 `3db2fce035dca0b5f1ac24917a24104640c1ab36f870f6c0b009a01386390ab9`, ClientSHA256 `45b6f1be674b5d9ce8185ea651dcb220b44d41ab3313e25755ac05690a340299`

Backendสุดท้าย270/270ผ่าน (TRX `commerce_net10.0_20261020100740.trx`), frontendunit33/33, types/buildผ่าน, browserSSO/Localpurchase2/2และUATnotice1/1, PWApolicy3/3 และshippingmanifest12hashตรง. BrowserPWAupdateรอบนี้ไม่ได้รันเพราะport5176มีLocalpreviewใช้งานอยู่ ไม่หยุดเซิร์ฟเวอร์ของผู้ใช้เพื่อทดสอบซ้ำ; ไม่ยกผลpolicyแทนbrowserE2E

UATสุดท้ายrelease`20261020-bank-pya-final`, APIgeneration28, archiveSHA256 `bb3249d1c0b6b20148aa126e32b91fb4701c6b7b823fb44e13f58b062857ef90`. ทุกdeployment9/9ready, DBready/paymentsReadyfalse/reportdisabled. Financial/effect16กลุ่มยัง0, shopperเดิม1; sessionsเพิ่ม3→8ระหว่างงาน (ไม่ลบ sessionหรือข้อมูลผู้ใช้). Diagnosticใช้Sequencesเท่านั้น ไม่สร้างOrder/Attemptหรือหลักฐานจ่ายปลอม

KKUpublic9GETเทียบbaselineแล้วstatus/bytes/SHA256ตรงทั้งหมด (ใช้browserUserAgent; CloudflareปฏิเสธPythonUserAgent1010 จึงไม่เอา403นั้นตัดสินว่าKKUเสีย). KKU/Portal/PortalConfig/sharedIngressServiceUID-generation-resourceVersionตรงbaseline. หลักฐานเครื่องอยู่ `.artifacts/bank-update/` ไม่อยู่Git. ยังไม่ถือว่าจ่ายครบวงจรหรือผ่านProduction

ข้อความประสานผล401แบบสุภาพอยู่ `Provisioning/TTB_VERIFY_401_HANDOFF_TH.md` พร้อมpublic key ส่งให้ธนาคารได้เอง ไม่ถาม Merchant/Biller/issuer/สิทธิ์ซ้ำ ไม่กล่าวยืนยันสาเหตุ401ล่วงหน้า
