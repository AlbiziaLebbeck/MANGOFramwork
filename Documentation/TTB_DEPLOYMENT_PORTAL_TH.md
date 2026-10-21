# TTB: BaseWorld และ Deployment Portal

อัปเดต 21 ตุลาคม 2026 — เปิดทดสอบ checkout หลัง TTB ลงทะเบียน public key

## UAT หลังลงทะเบียน public key (21 ตุลาคม 2026)

Verify จาก ntserver02 ผ่าน HTTP200/0002/NotFound พร้อม authenticatedBankReply=true. Backend release `20261021-uat-checkout-v1` ผ่าน CI279/279 และ deploy API/commerce worker/receipt worker ครบ; migration ไม่เพิ่ม schema. เปิด test checkout เฉพาะ UAT โดยใช้ชื่อแสดงผลชั่วคราว `inam cafe` ตามผู้ใช้ระบุ. Public config เป็น paymentsReady=true, Bank, mockSimulation=false; callbackValidated และ bankAcceptanceComplete ยังfalseจนกว่าจะมีหลักฐานจริง.

Portal server `mangos/metaverse-server-ttb-uat` ผูก game identity UAT เดิมแล้ว และตรวจจาก network ของ Unity pod ไป private API ได้HTTP200. Helper `Infrastructure/TTB/uat/configure-portal-game.py` รองรับ dry-run/--apply สำหรับslugนี้เท่านั้น และต้องตรวจอีกครั้งหาก Portal redeploy ล้างenv. ไม่ต้อง Build Unity ใหม่สำหรับการเปิด checkout รอบนี้. รายละเอียดหลักฐาน/การส่งต่อ callback อยู่ใน [TTB_UAT_GATEWAY_ACCEPTANCE_20261021_TH.md](TTB_UAT_GATEWAY_ACCEPTANCE_20261021_TH.md). ข้อความ paymentsReady=false/ยังไม่ตั้งgame identityด้านล่างเป็นประวัติวันที่20ตุลาคม.

อัปเดตตามคำยืนยันTTBเวลา11:02น.: callbackใช้profile `BankSourceIp` ซึ่งไม่ต้องส่งusername/passwordหรือAuthorization และยังตรวจต้นทางธนาคารผ่านHTTPS/allowlist/trustedproxyเดิม. ข้อมูลBasicที่เตรียมก่อนหน้าเลิกใช้ในUATหลังเปิดmodeนี้; ข้อมูลส่งต่อปัจจุบันอยู่ที่ [Provisioning/TTB_UAT_CALLBACK_HANDOFF_TH.md](Provisioning/TTB_UAT_CALLBACK_HANDOFF_TH.md). ไม่ต้องBuildUnityเพื่อเปลี่ยนauthenticationของcallbackฝั่งbackend.

## การเข้าถึงจากทุกเครือข่าย (20 ตุลาคม 2026 ช่วงค่ำ)

ผู้ใช้อนุญาตให้ปรับการเข้าถึงให้เหมาะกับเว็บที่เผยแพร่สาธารณะ หลังพบว่า IP ปัจจุบัน `27.130.101.22` ถูก ingress ปฏิเสธด้วย403 เพราะ preview เดิมอนุญาตเฉพาะ `58.11.26.155/32`.

ใช้ `Infrastructure/TTB/uat/shopper-ingress.yaml` แยก Ingress `ttb-shopper-public-uat` สำหรับ shopper API บน `ttb-api-test.mangosgo.com`: config, catalog, session/logout/game-ticket, cart, checkout, orders, inventory และ documents. เส้นทางเหล่านี้ไม่ผูก IP ผู้ใช้ จึงเปลี่ยน Wi-Fi/เครือข่ายมือถือได้โดยไม่ต้องเพิ่ม IP หรือ Build Unity ใหม่. API ยังคงตรวจ MANGOs identity, ownership, trusted Origin และ CSRF สำหรับรายการที่ต้องเข้าสู่ระบบ รวมทั้งมี ingress rate/connection limits.

Ingress `ttb-uat-world-preview` เดิมยังจำกัด IP สำหรับ preview, API admin, game-service และ local-session. Bank callback ยังใช้ ingress/allowlist ธนาคารเดิม. ไม่ใช้ regex/rewrite, ไม่เปลี่ยน shared ingress controller, TLS, CORS origins, namespace/network policy หรือ credentials. ข้อความด้านล่างที่กล่าวว่า shopper API ทั้งหมดจำกัด IP เดิมเป็นประวัติก่อนการเปลี่ยนครั้งนี้.

ย้อนกลับโดยลบเฉพาะ `kubectl -n ttb-uat delete ingress ttb-shopper-public-uat`; เส้นทางจะกลับไปใช้ preview ingress และ allowlist เดิมโดยอัตโนมัติ. ห้ามใช้ตัวอย่าง ingress แบบเปิดทั้ง `/ttb-commerce-test/v1` แทน manifest นี้ เพราะจะเปิดเครือข่ายให้ route ผู้ดูแลและ game-service ไปด้วย.

นำขึ้น ntserver02 แล้วและตรวจจาก PC กับ public egress ของ ntserver02 ผ่าน: config/catalog/session200, สินค้า3รายการ, OPTIONS204พร้อมcredentials/headersที่หน้าเว็บใช้, route ส่วนตัว401เมื่อไม่ลงชื่อเข้าใช้, Origin ที่ไม่อนุญาตถูกปฏิเสธ, admin/game/local-session/preview/bank callback403. ตรวจ path traversal ไปadminแล้วยัง403. UID/resourceVersion ของ12resourceเดิม (รวมcallback, proxy config, API policy, shared ingress Service/Deployment, KKU และ Unity Server) คงเดิมทั้งหมด.

Edge headless ผ่านPlaywrightบน `https://plus.mangosgo.com/ttb-uat/` ที่1280×720และ390×844: headerไม่มีข้อความเชื่อมต่อไม่ได้, config/catalog200, nativeUnityรายงาน `MANGOS_CLIENT_STATE Started` ผ่านWSSจริง, กดประวัติแล้วแสดงMANGOs sign-in. ไม่มีcommerce request failureหรือruntime errorที่เกี่ยวข้อง; anonymous401เป็นผลปกติ และfaviconของrootโดเมน404เป็นข้อบกพร่องเดิมที่ไม่เกี่ยวกับPayment. ยังไม่ได้ยืนยันMANGOs sessionที่ลงชื่อเข้าใช้จริง, ข้อมูลข้ามบัญชีหรือการชำระเงินธนาคาร; `paymentsReady=false` คงเดิม. Game service identity ของServerจากPortalยังต้องตั้งตามหัวข้อด้านล่าง ไม่ได้เพิ่มสิทธิ์game backendในงานแก้public shopperครั้งนี้.

## Build เองหลังแก้ CDN/PWA (20 ตุลาคม 10:30 น.)

พบว่าเว็บ `/ttb-uat/` ใช้ JavaScript SHA256 `839e2125e9320935a1285246f1d327b5b4a74fc29c05adb6bac1c42d8257b1a0` และ release `5c97b1fb90908ba7eda630703fb09563d02d61873c47b55071c34605b5dce9e0` ตรงกับชุด720bเดิมแล้ว แต่ HTMLที่Cloudflareส่งมีสคริปต์analyticsเพิ่ม:3230→3597bytes ทำให้hashของindexไม่ตรงและPWAอัปเดตไม่สำเร็จ. จึงไม่ควรสรุปว่าPortalไม่ได้อัปโหลดเพียงเพราะUIเดิมยังแสดงอยู่

แก้แล้ว: PWAอ่านHTMLต้นฉบับจาก`mango-ttb-shell.json`และตรวจขนาด/SHA256ก่อนcacheเป็นHTML ใช้releaseในURLของIIFE/CSS/config/workerเพื่อเลี่ยงCDNaliasเก่า ยังคงไม่cacheAPI/auth/เอกสาร/Unityruntimeหรือqueryทั่วไป. ทดสอบจำลองhostingเพิ่มscript+CDNเก็บIIFE/workerเก่า ผ่านbrowser1/1; policy4/4/types/unit33/33ผ่าน. ไม่แก้Cloudflare/Portal/KKUหรือCORSpolicy

**ต้อง Build ใหม่จากsourceหลังแก้รอบนี้** ชุด720bด้านล่างเป็นประวัติก่อนแก้CDN/PWAแล้ว ยังไม่เผยแพร่ชุดใหม่ให้ผู้ใช้โดยอัตโนมัติ

วิธีหลัก — คลิกจาก Unity ได้เลย ไม่ต้องรัน PowerShell:

1. บันทึกscene/assetsที่แก้อยู่
2. คลิก `MANGOs Framework > TTB > Build TTB UAT (Server + Client)` หรือเปิด `MANGOs Framework > Build Center` แล้วกดปุ่ม `Build TTB UAT (Server + Client)`
3. ปุ่มเตรียม HTML/React UI ล่าสุดให้อัตโนมัติ แล้ว Build Linux Server กับ WebGL Client และสร้าง ZIP ทั้งคู่ หากเตรียม UI ไม่สำเร็จจะหยุดก่อนเริ่ม Build เพื่อไม่ให้ใช้ UI เก่าค้างอยู่
4. รอผล `Succeeded` แล้วกด `Open Builds Folder` ใช้คู่จาก `Builds/mangos-ttb-uat/<build-id>/Upload/` ของงานเดียวกัน ไฟล์ `Builds/last-successful.json` บอกโฟลเดอร์ล่าสุด

เครื่องนี้มี Node.js และ dependencies พร้อมแล้ว ปุ่มเรียก Node โดยตรง ไม่ต้องเปิด terminal หรือมี PowerShell 7/pnpm อยู่ใน PATH ของ Unity. บันทึก log เตรียม UI ที่ `Library/TTB/overlay-build.log`. ไม่ปิด Editor หรือสร้างสำเนา Library เพิ่มสำหรับวิธีหลักนี้

แพ็กเกจ TTB ใหม่บีบอัด public WebGL เป็น Brotli และใช้ชื่อไฟล์ตาม SHA256 รวมถึงเก็บ cache เฉพาะข้อมูลฉากที่ชื่อเป็น hash. ตรวจ server เดิมของ Portal แล้วรองรับ `.br` พร้อม `Content-Encoding: br` และ MIME ที่ถูกต้อง จึงไม่ต้องแก้ระบบกลางหรือ KKU. ดูผลและข้อจำกัดใน `TTB_LOADING_AND_UI_20261020_TH.md`

วิธีที่2 — คำสั่งเดียวในPowerShell7 (ใช้สำเนาแยก ไม่ปิดEditorที่เปิดอยู่):

```powershell
Set-Location C:\CosmoPC\Work\Unity\MANGOxTTB
./Tools/TTB/build-portal.ps1
```

คำสั่งนี้เตรียมoverlayล่าสุดให้อัตโนมัติแล้วก่อนBuildBoth ผลอยู่`Builds/TTB/Portal/<run-id>/Release/Upload/`. ต้องSaveProfileในEditorก่อนเพื่อให้scriptอ่านprofileที่บันทึกแล้ว ห้ามเริ่มงานซ้อนในBuildWorkspaceเดียวกัน

นำ`ttb-uat-server.zip`และ`ttb-uat-client.zip`คู่ใหม่ขึ้นPortal ใช้slug/versionเดียวกันและตรวจว่าversionนั้นถูกเผยแพร่แล้ว เปิดURLที่Portalแสดง. หลังอัปโหลดครั้งแรกจากPWAรุ่นเดิม ให้ปิดแท็บTTBเดิมแล้วเปิดหน้าผ่านCtrl+Shift+R; ถ้ายังค้างให้DevTools > Application > Service Workers > Unregister **เฉพาะscope `/ttb-uat/`** แล้วเปิดใหม่ เก็บcookies/MANGOsloginไว้ ไม่ลบstorageทั้งโดเมนหรือworkerของKKU. ควรเห็นตัวเลือกไทย/Englishบนheader

## CORS ที่ตรวจจริง

`Origin: https://plus.mangosgo.com` → API`https://ttb-api-test.mangosgo.com/ttb-commerce-test/v1`: OPTIONS`/session`204, GET`/storefront/config`200, `Access-Control-Allow-Origin`ตรงorigin, `Access-Control-Allow-Credentials:true`, headers`content-type,x-csrf-token`อนุญาต. Originที่ไม่อนุญาตไม่มีACAO. ไม่ใช้wildcardร่วมcredentialsและไม่ใส่slug/pathในOrigin. เปลี่ยนslug/versionภายในplusโดเมนเดิมไม่ต้องเพิ่มCORSorigin

APIUATยังจำกัดIPทดสอบเดิม58.11.26.155/32 หากใช้เครือข่ายอื่นจนถูกingress403เบราว์เซอร์อาจแสดงอาการคล้ายCORS ต้องตรวจHTTPstatusก่อน ไม่แก้ด้วยการเปิดทุกorigin/IP. การทดสอบนี้เป็นpreflight/GET ไม่มีออเดอร์หรือbankrequest

## ฉากที่เปิดแก้ไข

เปิด `Assets/_Project/Scenes/BaseWorld.unity` ตามปกติ ใน Hierarchy มี `TTB commerce zone` และ `MangoTtbCommerceBridge` แล้ว ตัวร้านอยู่ที่ `(0,0,8)` และมีสินค้าสาธิตสามชิ้น หน้าชมสินค้า หมุน/ซูม ตะกร้า ประวัติ และใบเสร็จใช้ HTML overlay

สาเหตุที่ก่อนหน้านี้ BaseWorld ดูว่าง: สคริปต์ TTB รุ่นแรกเพิ่มร้านค้าในฉากสำเนาสำหรับ Build เท่านั้น รอบนี้ติดตั้งร้านค้าใน BaseWorld ของโปรเจกต์จริงและบันทึกผ่าน Editor แล้ว คง scene GUID, WorldManager prefab และ Build Settings เดิม มีสำรองก่อนแก้ที่ `.artifacts/scene-qa/project-world-25691020-005556-5e7983/` อย่านำสำรองเก่าทับฉากที่แก้ต่อแล้วโดยไม่เปรียบเทียบ

เมนู `MANGOs Framework > TTB > Validate project BaseWorld` ตรวจ binding สินค้า ไฟ bridge และเส้นทางฉาก หากนำไปใช้กับ checkout ที่ยังไม่มีร้านค้า มีเมนู `Install gallery in project BaseWorld` โดยต้องบันทึกงานค้างก่อน

## ชุดเก่าที่ Build และทดสอบแล้ว (อ้างอิงประวัติเท่านั้น)

รายการโฟลเดอร์เก่าด้านล่างเป็นหลักฐานก่อนแก้ UI/การโหลดล่าสุด บางโฟลเดอร์ถูกลบแล้วตามคำขอคืนพื้นที่ **อย่าใช้เส้นทางเหล่านี้เลือกไฟล์อัปโหลด** ให้ใช้ `Builds/last-successful.json` หลัง Build รอบใหม่และ ZIP ของงานนั้น

โฟลเดอร์ **`Builds/TTB/Portal/720b6fb4df314d22a0fc0258f0fb3824/Release/Upload/`** เป็นชุดก่อนแก้CDN/PWA รวม Auth public settings, SSO, ไทย–อังกฤษ และคำอธิบายUAT. ตรวจพบว่าขึ้นเว็บจริงแล้วตามhash แต่ควรBuildชุดใหม่ด้วยขั้นตอนด้านบนเพื่อรวมการแก้PWA มี:

| อัปโหลดเป็น | ไฟล์ | ขนาด | SHA256 |
|---|---|---:|---|
| Server | `ttb-uat-server.zip` | 61,880,657 bytes | `3db2fce035dca0b5f1ac24917a24104640c1ab36f870f6c0b009a01386390ab9` |
| Client | `ttb-uat-client.zip` | 35,128,883 bytes | `45b6f1be674b5d9ce8185ea651dcb220b44d41ab3313e25755ac05690a340299` |

Server เป็น Linux IL2CPP `.x86_64` พร้อม `_Data`, `GameAssembly.so`, `UnityPlayer.so` Client มี `index.html`, `Build/`, `TemplateData/`, HTML commerce และ PWA อยู่ครบในราก ZIP ตัดโฟลเดอร์ debug/backup ที่ Unity ระบุ DoNotShip ออกจาก ZIP แล้ว ไม่ต้อง ZIP `.x86_64` เพียงไฟล์เดียว และไม่ต้องอัปโหลดโฟลเดอร์ `Release` ครอบอีกชั้น

ทั้งสองไฟล์เป็น **UAT ชุดเดียวกัน** และใช้ BaseWorld/meta fingerprint เดียวกัน ไม่ใช้ ZIP ของ run เก่าปะปน

ชุด `a572d58dd99246d2bbf5dcdc349bcd6e` เป็นประวัติรุ่นSSO/ภาษาก่อนคำตอบธนาคาร ชุด `01e6564154604c78979d07bc55d669d6` และผลตรวจ08:22ด้านล่างเก่ากว่านั้น ไม่ใช้แทนชุดล่าสุด. Paired buildใหม่ผ่านและprotected5ไฟล์เดิม; browserQAของข้อความใหม่ผ่านLocaldisplayfixture ไม่อ้างว่าbankscan/Portalpublicacceptanceผ่าน

## อัปโหลดใน Portal

1. เข้า [Deployment Portal](https://deploy-plus.mangosgo.com/login) ด้วยบัญชีของผู้ดูแล
2. กำหนด slug ใหม่สำหรับ TTB UAT แล้วอัปโหลด Server ZIP และ Client ZIP ข้างต้นในช่องของแต่ละประเภท ใช้ **slug และ version เดียวกันทั้งคู่**
3. หาก Portal ให้เลือก executable ให้เลือกไฟล์ `.x86_64` ใน Server package แล้วดำเนินขั้นตอนเตรียม/เผยแพร่ของ Portal ตรวจสถานะ Server และ Client ให้สำเร็จทั้งคู่
4. ใช้ URL ที่ Portal แสดง เช่น `https://plus.mangosgo.com/<slug>/` หรือ `https://plus.mangosgo.com/<slug>/<version>/` Client จะอ่าน slug/version จาก URL แล้วเชื่อม `wss://multiplayer-plus.mangosgo.com/<slug>[/<version>]` อัตโนมัติ Server ภายในฟัง WS port `7770`

อ่าน backend Portal บน ntserver02 แล้วพบว่ารองรับโครงสร้าง ZIP นี้ และตั้งพอร์ต/เส้นทางตามข้างต้น รอบนี้ **ยังไม่ได้อัปโหลด สร้าง slug หรือเผยแพร่ผ่าน Portal** ชื่อ `ttb-portal-qa/v1` ในหลักฐานเป็น URL จำลองของการทดสอบส่วนตัว ไม่ใช่ slug ที่เผยแพร่แล้ว

## เชื่อม Server ของ slug ใหม่กับ commerce API

Portal ปัจจุบันใส่ network/slug configuration ให้เกม แต่ไม่ได้ใส่ game service identity ของ TTB ให้โดยอัตโนมัติ หลังได้ชื่อ Deployment ของ slug ที่สร้างจริง ต้องตั้งค่าต่อไปนี้เฉพาะ Server TTB ตัวนั้นบน ntserver02:

| ตัวแปร | ค่า/แหล่งข้อมูลสำหรับ UAT |
|---|---|
| `MANGOS_TTB_GAME_ENVIRONMENT` | `Uat` |
| `MANGOS_TTB_GAME_API` | `http://ttb-commerce-api.ttb-uat.svc.cluster.local:8080/ttb-commerce-test/v1` |
| `MANGOS_TTB_GAME_ID` | identity ที่ลงทะเบียนไว้ใน commerce UAT สำหรับ Server นี้ |
| `MANGOS_TTB_GAME_KEY` | อ้างอิง Kubernetes Secret ของ game identity ผ่าน `secretKeyRef` |

Deployment เกมที่ Portal สร้างอยู่ namespace `mangos` ส่วน commerce API อยู่ `ttb-uat` จึงต้องเตรียม Secret ใน namespace ของเกม และอนุญาต network เฉพาะ Deployment TTB นี้ไป API port `8080` ด้วย ตรวจ selector ของ policy กับ Deployment ที่มีจริงก่อน apply ไม่ขยายสิทธิ์ให้เกมทุกตัวหรือแก้ KKU/shared Portal เพื่อทำขั้นตอนนี้

ส่วนนี้ใช้ตรวจสิทธิ์สินค้าและการนำสินค้าดิจิทัลมาใช้ในโลก จึงต้องตรวจ game entitlement/lease หลังผูก identity แล้ว ไม่ถือว่าเพียงเปิดหน้าโลกได้เท่ากับผ่าน flow นี้ เว็บ Client ใช้ public UAT API ตาม profile อยู่แล้ว และ CORS สำหรับ `https://plus.mangosgo.com` ตรวจ preflight ได้ `204` พร้อม origin/credentials ที่ถูกต้อง โดยไม่ได้แก้ backend

UAT API ยังจำกัด IP ทดสอบ `58.11.26.155/32` ตามที่อนุญาตไว้ ดังนั้น URL Client ที่ Portal เผยแพร่ไม่ได้ทำให้ commerce API เปิดสู่ทุก IP โดยอัตโนมัติ ไม่ใส่ bank key, game key หรือรหัสผ่าน Portal ลง WebGL/ZIP

## Build รอบต่อไป

ใน Unity เปิด `MANGOs Framework > Build Center`:

ปุ่ม Build ของ TTB ใน Editor เตรียม HTML bundle ล่าสุดให้อัตโนมัติแล้ว ไม่ต้องรันคำสั่งก่อนกด Build

1. ตั้ง `TTB commerce = Uat` หรือใช้ `Use Deployment Portal UAT defaults`
2. เปิด `Deployment Portal slug routing` ตรวจ Server WS `7770` และ Client WSS `443`, host `multiplayer-plus.mangosgo.com` แล้ว Save Profile
3. กด `Build Both` เพื่อได้ Server/Client ที่ใช้ฉากชุดเดียวกัน รอทั้งสอง target และขั้นตอนสร้าง ZIP สำเร็จ ผลอยู่ `Builds/<deployment>/<build-id>/Upload/`

หากต้องการ Build แยกจาก Editor ที่เปิดใช้งานอยู่ เรียก PowerShell 7 จากรากโปรเจกต์:

```powershell
& Tools/TTB/build-portal.ps1
```

สคริปต์ใช้สำเนา `Builds/TTB/BuildWorkspace` กับ pipeline เดียวกับ Build Center ไม่ปิด Editor ของผู้ใช้ ต้องบันทึกฉาก/SaveProfileก่อนเริ่ม และscriptจะcompileHTMLoverlayล่าสุดให้อัตโนมัติ ห้ามรันงานสองชุดพร้อมกันในสำเนานี้ ผลอยู่ `Builds/TTB/Portal/<run-id>/Release/Upload/` งาน generic เดิมใช้ `TTB commerce = None`

## ผลตรวจและสิ่งที่ยังต้องตรวจหลังเผยแพร่

- Unity Server และ WebGL build สำเร็จทั้งคู่ ไม่มี build error ตรวจโครงสร้าง/hash ZIP และ protected root files แล้ว
- ใช้ Client bytes จากชุดสุดท้ายกับ Linux Server ชุดเดียวกันบน ntserver02 ผ่าน private native browser QA `1/1` ใน `31.5s`: เข้า guest, ต่อ Server, เดิน, คลิกโมเดลจริง, หมุน/ซูม, จอ `390×844`, เปิด cart ที่ต้องลงชื่อเข้าใช้ และคืน HUD/input เมื่อปิด overlay ไม่มี commerce mutation หรือ error ที่ไม่คาดหมาย ภาพ desktop/mobile/คืนโลกเปิดตรวจแล้ว
- QA นี้ fulfill static files จาก package และ relay WebSocket ผ่าน private SSH โดยคง frames เดิม จึงพิสูจน์ route ที่ Client เลือกและ native runtime แต่ยังไม่ใช่ผลของ Portal ที่เผยแพร่จริง, public WSS/TLS ของ slug ใหม่, MANGOs account ที่ลงชื่อเข้าใช้จริง, PWA ผ่าน public hosting หรือโทรศัพท์จริง
- ปิด SSH/static server และลบ Job/Pod/NetworkPolicy ชั่วคราวแล้ว UAT เดิมพร้อม 9 deployments, monitoring 3 Up/32 healthy, backup ทุก 15 นาที และ financial/identity aggregates 18 กลุ่มยังเป็นศูนย์ Portal generation/ConfigMap, shared ingress และ KKU 9 GET hashes ไม่เปลี่ยน

หลักฐานรวม: `.artifacts/portal-qa/acceptance-20261020.json` และ `Builds/TTB/Portal/01e6564154604c78979d07bc55d669d6/result.json` ภาพและ browser report อยู่ที่ `C:/Users/PC/.codex/visualizations/2026/10/17/01a14abb-1984-7073-9312-25b2c6e8f444/ttb-portal-20261020-0818/`

**การรับชำระเงินจริงยังปิด** (`paymentMode=Bank`, `paymentsReady=false`) รอข้อมูล/การลงทะเบียนและการทดสอบร่วมกับ TTB ตาม `TTB_REAL_PAYMENT_READINESS_TH.md` แพ็กเกจนี้ไม่ได้เปิด callback หรือ Production และการ Build สำเร็จไม่ได้รับรองว่าสแกน QR ด้วยแอปธนาคารจริงได้แล้ว
