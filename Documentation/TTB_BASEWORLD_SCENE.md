# BaseWorld: MANGOs gallery

## ปัจจุบัน: BaseWorld ในโปรเจกต์และ Portal (20 ตุลาคม 08:22 น.)

ติดตั้งร้านค้าใน **`Assets/_Project/Scenes/BaseWorld.unity` ของโปรเจกต์จริงแล้ว** เปิด Editor เห็น `TTB commerce zone` และ `MangoTtbCommerceBridge` แก้ฉากและ Build ผ่านเมนูปกติได้ คง scene GUID, WorldManager prefab และ Build Settings เดิม มีสำรองก่อนแก้และหลักฐาน hash ใน `.artifacts/portal-qa/editor-world-binding.json`

Build Center เพิ่ม `TTB commerce = Uat` กับ `Deployment Portal slug routing` แล้ว Paired Linux `.x86_64`/WebGL shipping ZIP run `01e6564154604c78979d07bc55d669d6` build สำเร็จและ private native QA ผ่าน `1/1` รวมหมุน/ซูม/mobile/คืน HUD ดูไฟล์อัปโหลดและการผูก game API หลังเลือก slug ใน [คู่มือ Portal](TTB_DEPLOYMENT_PORTAL_TH.md) ยังไม่ได้เผยแพร่ผ่าน Portal หรือเปิดรับเงินจริง

หัวข้อสคริปต์ Local/Generated ด้านล่างเป็นประวัติและ workflow แยกสำหรับ Local/UAT เดิม ข้อความที่ระบุว่าไม่แก้ original BaseWorld อธิบายรอบเหล่านั้น ปัจจุบัน workflow Portal ใช้ฉากต้นฉบับที่ติดตั้งแล้วตามหัวข้อนี้

## ฉากที่เตรียม

`TtbBaseWorldExhibition` สร้างพื้นที่แสดงสินค้าสไตล์แกลเลอรี: พื้นหินสีอ่อน ผนังงาช้าง ซุ้มโค้งสามช่อง สีเขียวเข้ม ขอบทองเหลือง คานไม้ ช่องเปิดรับแสง ม้านั่ง และกระถางใบมะม่วง. ป้ายในโลกเป็นตัวอักษรสามมิติ; การชมสินค้า หมุน/ซูม ตะกร้า QR ประวัติ และใบเสร็จใช้ HTML overlay เดิม.

- พื้นที่ 20 × 20 เมตร ตำแหน่ง zone ใน BaseWorld `(0,0,8)`; ทางเข้าด้านหน้าเปิดกว้าง. พื้นอยู่เหนือ ground เดิมเล็กน้อยเพื่อไม่ให้ลายตารางซ้อนกับหิน
- สินค้าสาธิตสามชิ้นใช้ binding `orbit-lamp`, `mango-sculpture`, `mango-star` เดิม และ native product colliders. ตัวอย่างชื่อ/ประเภทไม่ใช่รายการสินค้าที่บริษัทอนุมัติขายจริง
- เพิ่มไฟสี่ดวง ไม่มีเงาจากไฟเหล่านี้; ใช้แสงอาทิตย์เดิม. ไฟของฉากไม่ส่อง layer 31 ซึ่งสงวนให้ preview สินค้า
- ผนัง พื้น แท่น ม้านั่ง และกระถางมี collider. ของตกแต่งย่อยไม่มี collider; ทางเดินและการชนยังต้องทดสอบกับ player จริง
- ใช้ URP Lit, TMP font และ procedural mesh ภายในโครงการ. ไม่ต้องดาวน์โหลด asset/texture เพิ่ม
- `TtbWorldAtmosphere` ปรับ ambient/sky/fog/sun ใน BaseWorld และคืนค่าที่จับไว้เมื่อออกจากฉาก หากฉากเดิมยัง active. ไม่แก้ lighting ของโลกใหม่ที่เปลี่ยนไปแล้ว

## ไฟล์และการ Build ของ workflow Local เดิม

ตัวสร้างฉากอยู่ `Assets/_TtbCommerce/Editor/TtbBaseWorldExhibition.cs`. Asset ที่สร้างเป็นของ TTB ใน `Assets/_TtbCommerce/Generated/World/` และ prefab `Assets/_TtbCommerce/Generated/TtbCommerceZone.prefab`.

ฉากเต็มที่เตรียมระหว่าง Build คือ **`Assets/_TtbCommerce/Generated/World/BaseWorld.unity`**. ชื่อ scene ยังเป็น `BaseWorld`. ฉากนี้คัดมาจาก BaseWorld ของโครงการแล้วเพิ่ม exhibition/bridge; Bootstrap, Launcher, network player และกล้องใช้ระบบเดิม. Build เต็มแทนที่ path BaseWorld ใน `BuildPlayerOptions` และ serialized `baseWorldScene` ของ WorldManager prefab **เฉพาะในสำเนาแยก** เพราะ loader เดิมใช้เต็ม path. ไม่เขียนทับไฟล์ต้นฉบับ `Assets/_Project/Scenes/BaseWorld.unity`, WorldManager prefab ของผู้ใช้ หรือรายการ scene ของ Editor ที่เปิดอยู่; ตรวจ hash ต้นฉบับก่อนคืนผล.

สั่งจากราก repository ด้วย PowerShell 7 และ Unity `6000.3.26f1` ที่มี Windows Dedicated Server/WebGL modules และ license ใช้งานได้:

```powershell
& Tools/TTB/build-local.ps1
& Tools/TTB/build-world-local.ps1 -Target Both
```

คำสั่งแรกสร้าง gallery สำหรับตรวจ native product preview และสร้าง shared exhibition assets/registry/player relay integrations. คำสั่งที่สองสร้าง Windows server กับ WebGL client โดยใช้ฉาก BaseWorld ที่เตรียม **ชุดเดียวกัน**. หลัง save-as ต้อง rebuild FishNet SceneIds ก่อน Build; Client ในรอบ `Both` ตรวจ SHA256 ของ scene และ metadata ให้ตรงกับ Server แล้วเปิดฉากที่เตรียมไว้โดยไม่สร้าง ID สุ่มซ้ำ. ใช้ `Both` สำหรับชุดที่จะเชื่อมต่อกัน; การ Build แยก target เดียวไม่รับรองว่า IDs ตรงกับ binary อีกชุดที่มีอยู่. ห้ามรันสองคำสั่งพร้อมกันใน BuildWorkspace และห้ามเขียนทับ server executable ที่กำลังใช้งาน; ตรวจตัวตนและหยุดเฉพาะ local server ที่เป็นของการทดสอบก่อน.

ผลอยู่ `Builds/TTB/LocalGallery` และ `Builds/TTB/LocalWorld/{Server,Client}`. Build ใช้ Local+Mock/loopback API 5286 และ game port 7778; ไม่ได้ deploy ไป UAT/Production และไม่เปิดรับเงินจริง. Log แต่ละรอบมีชื่อไม่ซ้ำใน `Builds/Logs/` เพื่อไม่ใช้หลักฐานของรอบเก่าแทนรอบใหม่.

Build ใช้สำเนา `Builds/TTB/BuildWorkspace` และ Library ของสำเนา ไม่ปิด Editor ของผู้ใช้. การ copy กลับจำกัด generated assets/resources และ player integrations ที่ตรวจแล้ว; ตรวจ hash ของ player ก่อนเขียนกลับ และตรวจต้นฉบับ BaseWorld ก่อนนำฉากที่เตรียมกลับ. มีตัวเฝ้ารอที่ถือ handle ของ process ที่เพิ่งเปิด; เมื่อพบ Licensing initialization failed และ reconnect ล้มเหลวซ้ำ จะหยุดเฉพาะ build นั้น ไม่ค้น/หยุด Unity ตามชื่อและไม่หยุด Licensing Client ร่วม.

## เกณฑ์ก่อนระบุว่าพร้อม Build

1. Unity สร้าง gallery และ Server/Client สำเร็จจริง ไม่มี compiler/build errors; ฉากที่เตรียมและ prefab/model bindings ครบ
2. เปิดภาพ WebGL จริงตรวจสี วัสดุ ป้าย ซุ้ม แสง ความสว่าง/การบังสินค้า และมุมจาก spawn/ทางเข้า. ภาพจากฉากเดิมหรือ offline C# compilation ใช้แทนไม่ได้
3. เดินตรวจทางเข้า พื้น ทางเดิน และการชนตัวอย่างด้วย standard player เดิม; กล้อง/การเลือกสินค้าไม่ถูกของตกแต่งขวาง. การบินและ collision ทุกเส้นทางต้องมี acceptance เพิ่ม ไม่ใช้ smoke test นี้รับรองทั้งหมด
4. คลิก native model เปิด HTML overlay, หมุน/ซูม desktop และ touch, ปิดแล้วคืน input/cursor; หน้าชมสินค้าไม่รับแสงจากโลกที่สงวนแยกไว้
5. ทดสอบ Local+Mock ซื้อ/ประวัติ/ใบเสร็จ และ game entitlement/lease/logout บน Server กับ Client ที่ Build ใหม่ตรงกัน; ตรวจ browser console/page errors และภาพจริง
6. ตรวจ hash ไฟล์เดิมที่ผู้ใช้แก้ค้าง และยืนยันไม่มีการเปลี่ยน KKU/Production

สถานะวันที่ 19 ต.ค. 2569: **พร้อม Build และทดสอบ Local+Mock ผ่านสคริปต์ข้างต้นแล้ว**. Unity CI final `9653a54b5537465d977d5a772fdc2346` สร้าง Gallery/Server/Client สำเร็จและตรวจ hash264artifacts/ไฟล์เดิม5รายการตรง. NativeGallery desktop/mobile3/3 ผ่าน; full-world1/1 ผ่านรวมคลิกสินค้าจริง เปิดnativepreview ใส่physical+digitalในตะกร้า สร้าง/จ่ายQRMock ดูประวัติ เซฟPDF187490bytes ต่อleaseและlogout. เดินW/A/W/Sและเปิดดูภาพ5ภาพผ่านเส้นทางเข้า/พื้น/ทางเดิน/sidewall/return; ไม่พบpage/console errors. ภาพจริงและproofผูกhashอยู่ `.artifacts/scene-qa/baseworld-final-20261019/` และ `world-walk-20261019/`.

Build รอบแรกติด Licensing แต่รอบที่ได้รับapprovalแยกทำงานได้ โดยไม่ปิดEditor/LicenseClientของผู้ใช้. ระหว่างตรวจพบsave-asต้องrebuildFishNetSceneIdsและต้องแก้pathที่WorldManagerเก็บไว้; แก้เฉพาะcopyและยืนยันruntimeแล้ว. การ Build สำเร็จอย่างเดียวไม่เคยใช้แทนผลworldtestที่ล้มเหลว. ยังไม่มีการ deploy ฉากไป NT/UAT/Production; การรับรองธนาคาร มือถือจริง การบินครบทุกกรณี และข้อมูลสินค้า/บริษัทจริงอยู่ใน acceptance ของระบบเต็มตามledger.

## พื้นที่ HTML overlay และ native UI (19 ตุลาคม)

WebGL template ใช้ `ttb-viewport.css` และ React วัด header/bottom nav จริงผ่าน ResizeObserver แล้วกันพื้นที่ canvas ด้านบน/ล่าง จึงไม่บัง native login หรือ world HUD. Product preview คำนวณ viewport เทียบ canvas rectangle ใหม่ ไม่ใช้ความสูงหน้าจอเต็ม Toast มีช่องว่างแยกจาก dialog, ปุ่มปิดใช้ sticky tools และ desktop history แยกการเลื่อนรายการ/รายละเอียดเพื่อไม่ให้รายชื่อออเดอร์หรือปุ่มปิดเลื่อนหายระหว่างอ่านเอกสาร

อัปเดตเฉพาะ HTML/React/PWA ของ binary Local ที่ Build ไว้แล้วได้ด้วย `Tools/TTB/update-local-overlay.ps1 -Target Both`. คำสั่งนี้คง native binaries และ scene IDs เดิม เพิ่ม stylesheet เข้า owned index เมื่อยังไม่มี และรวม stylesheet ใน PWA digest manifest. ไม่ต้องปิด Editor ของผู้ใช้เพื่อแก้ HTML/CSS

ฉากเดิมใช้ AreaSpawner สุ่มจุดเกิด ไม่ควรใช้พิกัดหน้าจอเดียวรับรองการคลิกสินค้าในทุก run. World test รอ native owning-player Ready และภาพกล้องนิ่ง แล้วคลิกส่วนสีของโมเดลที่มองเห็นจาก screenshot จริง ก่อนตรวจสินค้าที่เปิด; ไม่ส่ง product.open/Paid/ownership ปลอมเข้าระบบ. การซื้อ mixed cart ใช้ UI ตั้งแต่ตะกร้าว่าง แล้วจึง equip สินค้าที่เพิ่งซื้อและตรวจ lease/logout

Backend เพิ่มบริษัททดสอบที่เปลี่ยนภายหลังได้ และ Local demo ราคา1/10/99บาท ตาม `TTB_TEST_COMPANY_AND_CATALOG_TH.md`. Local Mock QR เป็นรหัสจำลอง ไม่ใช้รับเงินจริง; Production และ bank acceptance ยังแยกตามแผน


## UAT Linux และworldportrait 20 ตุลาคม

เพิ่มTools/TTB/build-world-uat.ps1สำหรับLinuxDedicatedServer+WebGLUATด้วยpreparedscene/metaเดิมและไม่copyconfigurationกลับroot. Initial privateNTdeployแล้วตามด้วยรุ่นแก้portrait `73b78e1e74064bf58a4b58504bb26e57`; browsernativeจริง1/1ผ่าน31.1s. พบportraitguardเดิมวาดทับpreviewในworld (Gallerymobileไม่ได้มีguardนี้) จึงแก้TtbWorldInputGateให้เก็บ/ปิดเฉพาะScreenSpaceCanvasขณะHTMLoverlayเปิดและคืนenabledstateเดิมเมื่อปิด. ภาพmodelmobileและคืนnativeHUDตรวจแล้ว; ไม่แก้originalOrientationManager/player source. CurrentLocalbinariesยังเป็นชุดก่อนหน้า; rootadapterพร้อมBuildใหม่ผ่านscriptLocalBothตามปกติ. รายละเอียด `TTB_UAT_WORLD_DEPLOYMENT.md`

20Oct04:07BKK: เปิด [UAT preview](https://ttb-api-test.mangosgo.com/store-uat/) ตามคำอนุญาตเฉพาะ58.11.26.155/32แล้ว. ActualHTTPS/WSS browser1/1ผ่าน28.0s, nativeclick/rotate/zoom/390×844preview/คืนHUDผ่านและภาพเปิดตรวจ; ไม่ใช้SSHrelay. IPอื่นรวมปลอมXFF403ครบ6กรณี. KKU/ProductionและsharedServiceไม่เปลี่ยน; bankapp/MANGOsrealaccount/โทรศัพท์จริงยังรอตรวจรับ
