# TTB: BaseWorld และ Deployment Portal

อัปเดต 20 ตุลาคม 2026 — รุ่นเชื่อมบัญชีอัตโนมัติและไทย–อังกฤษ

## ฉากที่เปิดแก้ไข

เปิด `Assets/_Project/Scenes/BaseWorld.unity` ตามปกติ ใน Hierarchy มี `TTB commerce zone` และ `MangoTtbCommerceBridge` แล้ว ตัวร้านอยู่ที่ `(0,0,8)` และมีสินค้าสาธิตสามชิ้น หน้าชมสินค้า หมุน/ซูม ตะกร้า ประวัติ และใบเสร็จใช้ HTML overlay

สาเหตุที่ก่อนหน้านี้ BaseWorld ดูว่าง: สคริปต์ TTB รุ่นแรกเพิ่มร้านค้าในฉากสำเนาสำหรับ Build เท่านั้น รอบนี้ติดตั้งร้านค้าใน BaseWorld ของโปรเจกต์จริงและบันทึกผ่าน Editor แล้ว คง scene GUID, WorldManager prefab และ Build Settings เดิม มีสำรองก่อนแก้ที่ `.artifacts/scene-qa/project-world-25691020-005556-5e7983/` อย่านำสำรองเก่าทับฉากที่แก้ต่อแล้วโดยไม่เปรียบเทียบ

เมนู `MANGOs Framework > TTB > Validate project BaseWorld` ตรวจ binding สินค้า ไฟ bridge และเส้นทางฉาก หากนำไปใช้กับ checkout ที่ยังไม่มีร้านค้า มีเมนู `Install gallery in project BaseWorld` โดยต้องบันทึกงานค้างก่อน

## ชุดที่ Build และทดสอบแล้ว

โฟลเดอร์ **`Builds/TTB/Portal/a572d58dd99246d2bbf5dcdc349bcd6e/Release/Upload/`** เป็นชุดล่าสุด รวม Auth public settings ที่ผู้ใช้แก้, การเชื่อม MANGOs SSO อัตโนมัติ และเมนูไทย–อังกฤษตาม `TTB_SSO_AND_LANGUAGE_TH.md` มี:

| อัปโหลดเป็น | ไฟล์ | ขนาด | SHA256 |
|---|---|---:|---|
| Server | `ttb-uat-server.zip` | 61,880,445 bytes | `5939cd424fb7d2c951bb9f4dc4d05456b41089c4938b18eaff5f9ba3c080903d` |
| Client | `ttb-uat-client.zip` | 35,128,629 bytes | `67063d4b21af34493e2c6d575e2c599f24dbc118735fe494ca60f24d0206d0d5` |

Server เป็น Linux IL2CPP `.x86_64` พร้อม `_Data`, `GameAssembly.so`, `UnityPlayer.so` Client มี `index.html`, `Build/`, `TemplateData/`, HTML commerce และ PWA อยู่ครบในราก ZIP ตัดโฟลเดอร์ debug/backup ที่ Unity ระบุ DoNotShip ออกจาก ZIP แล้ว ไม่ต้อง ZIP `.x86_64` เพียงไฟล์เดียว และไม่ต้องอัปโหลดโฟลเดอร์ `Release` ครอบอีกชั้น

ทั้งสองไฟล์เป็น **UAT ชุดเดียวกัน** และใช้ BaseWorld/meta fingerprint เดียวกัน ไม่ใช้ ZIP ของ run เก่าปะปน

ชุด `01e6564154604c78979d07bc55d669d6` และผลตรวจ 08:22 ด้านล่างเป็นประวัติรุ่นก่อนเพิ่ม SSO/ภาษา อย่าใช้ ZIP รุ่นนั้นแทนชุดล่าสุดหากต้องการการแก้สองเรื่องนี้

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

1. ตั้ง `TTB commerce = Uat` หรือใช้ `Use Deployment Portal UAT defaults`
2. เปิด `Deployment Portal slug routing` ตรวจ Server WS `7770` และ Client WSS `443`, host `multiplayer-plus.mangosgo.com` แล้ว Save Profile
3. กด `Build Both` เพื่อได้ Server/Client ที่ใช้ฉากชุดเดียวกัน รอทั้งสอง target และขั้นตอนสร้าง ZIP สำเร็จ ผลอยู่ `Builds/<deployment>/<build-id>/Upload/`

หากต้องการ Build แยกจาก Editor ที่เปิดใช้งานอยู่ เรียก PowerShell 7 จากรากโปรเจกต์:

```powershell
& Tools/TTB/build-portal.ps1
```

สคริปต์ใช้สำเนา `Builds/TTB/BuildWorkspace` กับ pipeline เดียวกับ Build Center ไม่ปิด Editor ของผู้ใช้ ต้องบันทึกฉากและ build HTML overlay ล่าสุดก่อนเริ่ม ห้ามรันงานสองชุดพร้อมกันในสำเนานี้ ผลอยู่ `Builds/TTB/Portal/<run-id>/Release/Upload/` งาน generic เดิมใช้ `TTB commerce = None`

## ผลตรวจและสิ่งที่ยังต้องตรวจหลังเผยแพร่

- Unity Server และ WebGL build สำเร็จทั้งคู่ ไม่มี build error ตรวจโครงสร้าง/hash ZIP และ protected root files แล้ว
- ใช้ Client bytes จากชุดสุดท้ายกับ Linux Server ชุดเดียวกันบน ntserver02 ผ่าน private native browser QA `1/1` ใน `31.5s`: เข้า guest, ต่อ Server, เดิน, คลิกโมเดลจริง, หมุน/ซูม, จอ `390×844`, เปิด cart ที่ต้องลงชื่อเข้าใช้ และคืน HUD/input เมื่อปิด overlay ไม่มี commerce mutation หรือ error ที่ไม่คาดหมาย ภาพ desktop/mobile/คืนโลกเปิดตรวจแล้ว
- QA นี้ fulfill static files จาก package และ relay WebSocket ผ่าน private SSH โดยคง frames เดิม จึงพิสูจน์ route ที่ Client เลือกและ native runtime แต่ยังไม่ใช่ผลของ Portal ที่เผยแพร่จริง, public WSS/TLS ของ slug ใหม่, MANGOs account ที่ลงชื่อเข้าใช้จริง, PWA ผ่าน public hosting หรือโทรศัพท์จริง
- ปิด SSH/static server และลบ Job/Pod/NetworkPolicy ชั่วคราวแล้ว UAT เดิมพร้อม 9 deployments, monitoring 3 Up/32 healthy, backup ทุก 15 นาที และ financial/identity aggregates 18 กลุ่มยังเป็นศูนย์ Portal generation/ConfigMap, shared ingress และ KKU 9 GET hashes ไม่เปลี่ยน

หลักฐานรวม: `.artifacts/portal-qa/acceptance-20261020.json` และ `Builds/TTB/Portal/01e6564154604c78979d07bc55d669d6/result.json` ภาพและ browser report อยู่ที่ `C:/Users/PC/.codex/visualizations/2026/10/17/01a14abb-1984-7073-9312-25b2c6e8f444/ttb-portal-20261020-0818/`

**การรับชำระเงินจริงยังปิด** (`paymentMode=Bank`, `paymentsReady=false`) รอข้อมูล/การลงทะเบียนและการทดสอบร่วมกับ TTB ตาม `TTB_REAL_PAYMENT_READINESS_TH.md` แพ็กเกจนี้ไม่ได้เปิด callback หรือ Production และการ Build สำเร็จไม่ได้รับรองว่าสแกน QR ด้วยแอปธนาคารจริงได้แล้ว
