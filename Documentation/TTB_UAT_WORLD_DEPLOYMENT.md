# โลก 3D บน UAT

20 ตุลาคม 2026: Linux Dedicated Server และ WebGL ทำงานจริงใน namespace `ttb-uat` ผ่าน private ClusterIP และหน้า HTTPS/WSS เฉพาะ IP ทดสอบ `58.11.26.155/32` ที่ผู้ใช้อนุญาตแล้ว. แยกจาก Windows Local+Mock และ KKU. Bank mode ยังปิดการชำระเพราะ bank profile ยังไม่ครบ

## Build และ deploy ที่ตรวจแล้ว

- Initial build/deploy `31ca27a7e0ef4e468c24d4768b4ea531`; ปัจจุบันใช้ `73b78e1e74064bf58a4b58504bb26e57`, archive SHA256 `34329eee38446cf2f3853b0de24b430fd152d88b69f89f9e1993ea726bf8218c`
- Buildผ่านLinux Server+WebGL, actual server API profile checks17กรณี, 218build artifacts/224packaged files. ใช้reviewed scene/metaชุดเดียวกันกับFishNet IDsเดิม: `EEC1FDCF43852AC223455C22AC8F06D633694D594A3B0C1E6F93C0C5A2E2DF64:00254AC26EED9F3F19CD844C80A3F447F64772CAB64F155CA8AB4DEB8D5CB94F`
- Buildใช้สำเนา `Builds/TTB/BuildWorkspace`; ไม่ปิดEditorและไม่เขียนกลับoriginal BaseWorld/WorldManager/protected userfiles. Linux/Local configurationอยู่ในสำเนา ไม่ใส่game keyหรือbank keyในWebGL
- Private deployments `ttb-world-uat`/`ttb-world-web-uat` generation2. UIDเดิมยังตรง; APIgeneration26คงเดิมในการupgradeครั้งนี้. Game secretไม่ได้rotate; service/policy/transportเดิมคงเดิม
- Gameใช้กุญแจserviceเฉพาะUATและpinned internalAPI `http://ttb-commerce-api.ttb-uat.svc.cluster.local:8080/ttb-commerce-test/v1`. ยอมรับHTTPนี้เฉพาะserver profileUat. NetworkPolicyจริงอนุญาตAPI/DNSและปฏิเสธPostgreSQLTCP; webไม่มีegress. ไม่mountbank/SQL secretsหรือprivate financial filesให้เกม/เว็บ
- Static serverตรวจhashpublic filesทุกไฟล์ก่อนเริ่ม, ไม่มีdirectory listing/API/uploads; gzipและWebAssembly MIMEตรวจจากbrowserจริง

คำสั่งสร้างรอบใหม่และupgradeแบบตรวจUID/resourceVersion:

```powershell
& Tools/TTB/build-world-uat.ps1 -RunId '<new-32hex>'
& 'C:/Users/PC/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe' Infrastructure/TTB/uat/package-world.py --run-id '<new-32hex>'
& Infrastructure/TTB/uat/deploy-world.ps1 -RunId '<new-32hex>' -Phase create -PreviousRunId '<verified-current-32hex>'
& Infrastructure/TTB/uat/deploy-world.ps1 -RunId '<same-new-32hex>' -Phase observe
```

ห้ามสร้างรอบซ้ำเพราะobserve timeout. เก็บrunเดิมและตรวจUID/phaseก่อน; initial createและupgradeมีpreconditionsต่างกัน. Operatorเก็บmanifest/state/resultใต้ `/opt/ttb-commerce/uat/world-releases/<run>/` และPC `.artifacts/uat-world/<run>/`

## Browser acceptance

Private UAT browser1/1ผ่าน31.1วินาที: ไฟล์เว็บจากNTจริง, WebSocketframesส่งผ่านSSHไปLinux FishNetจริงโดยไม่แก้payload, nativeGuest/owningRPC → nativeproductclick → native3Dpreview/rotate/zoom → HTML mobile layout → ต้องใช้MANGOsloginก่อนเพิ่มcart → ปิดoverlayคืนHUD/เดินได้. ภาพmobileมีorange5881/light86463pixelsและตรวจด้วยตาแล้ว โมเดลไม่ถูกportrait guardบัง

แก้เฉพาะadapter `TtbWorldInputGate` ให้เก็บ/ปิดScreenSpace canvasesขณะHTMLoverlayเปิดแล้วคืนenabled stateเดิมเมื่อปิด; WorldSpace canvasesคงเดิม. ไม่แก้OrientationManagerหรือplayer input sourceของผู้ใช้. ผลเก่าGallerymobileไม่ครอบคลุมworldportrait จึงเก็บfailureแรกที่พบblackguardและrerunหลังแก้ไว้ตามจริง

QAสองครั้งก่อนหน้าล้มจากตัวทดสอบ: ครั้งแรกบล็อกavatarGLBที่ระบบเดิมต้องโหลดและคลิกแนวที่avatarcapsuleบัง, ครั้งสองบล็อกreadonlyMANGOs `/users/me`. แก้QAให้โหลดassetจริง/เดินหลบด้วยcontrollerจริงและยอมรับanonymous401ตามURLเฉพาะ; ไม่ส่งidentity/paid/ownershipปลอม. Finalผลไม่มีunexpectedpage/consoleerrors, ไม่มีcommerce HTTPmutation/financial/grant. ไม่อ้างว่าMANGOs authenticated session, bank app, public TLS/WSS หรือphysical deviceผ่านจากการrelayนี้

## หน้า HTTPS/WSS สำหรับผู้ทดสอบ

ผู้ใช้อนุญาตเปิดเฉพาะ UAT/IP ทดสอบเดิมโดยตรงแล้ว หลัง automatic approval review เคยปฏิเสธการเปิด external boundary. คำสั่ง create ที่ผ่านการตรวจอนุมัติสร้าง Ingress `ttb-uat-world-preview` สำเร็จเพียงครั้งเดียวเมื่อ 20Oct04:02BKK; UID `d6b02c6d-f050-4df1-a3aa-ddd413f02943`. เรื่องรอคำอนุญาตเดิมจบแล้ว ไม่ต้องสร้าง Ingress ซ้ำ

เข้า [หน้า UAT](https://ttb-api-test.mangosgo.com/store-uat/) จากเครือข่ายที่ออก IP `58.11.26.155` เท่านั้น. เส้นทางที่เปิดคือ `/store-uat`, exact `/ttb-game-uat` และ `/ttb-commerce-test/v1`; ไม่มี callback/metrics/Production route เพิ่ม. ใช้ URL ที่ลงท้าย `/store-uat/` ตามที่ทดสอบ. ไม่แก้ shared Service, API NetworkPolicy, proxy ConfigMap หรือ health Ingress เดิม

Public browser 1/1 ผ่าน 28.0 วินาที เวลา04:05BKK โดยใช้ HTTPS/WSS จริง ไม่มี HTTP/WS relay หรือการข้าม TLS validation: 51 client frames/48 server frames, native guest/owning game RPC → คลิกโมเดลจริง → หมุน/ซูม → หน้าสินค้า390×844 → ปุ่มเพิ่มตะกร้าขอ MANGOs login → ปิดแล้วคืน HUD/เดินได้. ภาพ desktop1280×720 และ mobile390×844 เปิดตรวจแล้ว; mobile preview orange5173/light90439 pixels. Page/console unexpected errors, commerce HTTP mutations และ financial/grant เป็น0. Anonymous MANGOs `/users/me`401 เป็นผลที่คาดไว้เฉพาะ endpoint นี้. ปิด service worker ใน QA เพื่ออ่าน asset สด จึงไม่ใช้รอบนี้รับรอง public PWA update/offline, บัญชี MANGOs ที่ login จริง, โทรศัพท์จริง หรือ bank app

จาก ntserver02 ที่อยู่นอก IP อนุญาต ตรวจ GET ทั้ง3เส้นทางได้403ครบ6กรณี รวมปลอม `X-Forwarded-For:58.11.26.155`; CA/hostname ตรวจตามปกติ. จาก PC ที่อนุญาต shop/config/health ตอบ200 ส่วน `/metrics`, `/health/ready`, `/ttb/v1/credit-notification` ตอบ404. GET `/session/local`405 เป็นเพียงผล method ไม่ใช้ยืนยันว่า POST local login เปิดหรือปิด; ขอบเขต Local-only มี unit/integration gate แยก และ UAT ไม่แสดง Local tools

Postcheck04:07BKK: UAT9deployments/KKUReady1, monitoring3targetsUp/32rules evaluation healthy, backup Cron15นาทียังเปิด และ financial+identity18กลุ่มเป็น0. Shared Service UID/RV5847288/Local/31842เดิม. KKU9GET status/hash ตรง baseline ทุกไฟล์ (ไม่ทดสอบ authenticated sessions/active WebSocket). ไม่มี bank HTTP/payment request และไม่เปลี่ยน KKU/Production. Owned SSH preview tunnels ปิดก่อนทดสอบ public แล้ว

หลักฐานใน `.artifacts/uat-world/73b78e1e74064bf58a4b58504bb26e57/`: `preview-route-result.json`, `preview-public-http.json`, `preview-denied-source.json`, `postcheck-preview-20261020.json`, `kku-public-after-preview-20261020.json` และ `preview-acceptance-20261020.json`. Browser report/ภาพอยู่ `C:/Users/PC/.codex/visualizations/2026/10/17/01a14abb-1984-7073-9312-25b2c6e8f444/ttb-uat-public-20261020-0410/`

เรียก observer แบบอ่านอย่างเดียวผ่าน SSH ได้ด้วย `Get-Content Infrastructure/TTB/uat/verify-world-preview.py -Raw | ssh ntserver02 'python3 -'`. ตัวนี้ตรวจ health/aggregate counts/monitoring/topology; ใช้ `enable-world-preview.py --phase observe` ตรวจ UID/spec/annotations และ existing transport identity ของ run เดิมเพิ่มเติม. ห้ามเรียก create ซ้ำเพื่อแก้ observation timeout
