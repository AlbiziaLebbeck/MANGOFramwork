# เตรียมสินค้า UAT

ผู้ใช้อนุญาตสินค้า mockup ที่มีราคาตั้งแต่1บาท และผู้ขายรายเดียวรับเงินบริษัทเดียว ขั้นเตรียม catalog แยกจาก Local bank simulator โดยเด็ดขาด

## ข้อมูลที่จะเตรียม

| modelKey | สินค้า | ประเภท | ราคา | สต็อกจำลองเริ่มต้น |
|---|---|---|---:|---:|
| orbit-lamp | Orbit Lamp | Physical | 99 บาท | 20 |
| mango-sculpture | Mango Sculpture | Physical | 10 บาท | 20 |
| mango-star | Mango Star — Digital | Digital InventoryItem | 1 บาท | ไม่ติดตามสต็อก |

SKU มี prefix `UAT-`. รายละเอียดระบุชัดว่าเป็นสินค้าจำลอง UAT ไม่มีการส่งมอบสินค้าจริง ไม่มีการสร้าง order, QR, bank credit, allocation, receipt, entitlement หรือ outbox jobจากขั้นเตรียมนี้

## เงื่อนไขและคำสั่ง

1. Deploy release ที่ผ่าน full Backend CI และมี capability `uat-test-catalog-v1` ก่อน
2. Existing API ต้องเป็น UAT/Bank ที่พร้อมและไม่มี rolloutค้าง, `paymentsReady:false`, ไม่ freeze และ financial tables ยังว่าง. Command ปฏิเสธ Local/Mock/Production และฐานที่ไม่มี existing UAT marker
3. ใช้ run ID ใหม่ครั้งเดียว แล้ว observe **run เดิม** จน Jobมีผลจริง ห้ามสร้าง Jobใหม่เพราะอ่านสถานะ timeout

```powershell
$catalogRunId = [Guid]::NewGuid().ToString('N')
& Infrastructure/TTB/uat/prepare-test-catalog.ps1 -RunId $catalogRunId -ReleaseId '<deployed-uat-release>' -Phase create
& Infrastructure/TTB/uat/prepare-test-catalog.ps1 -RunId $catalogRunId -ReleaseId '<deployed-uat-release>' -Phase observe
```

ตัว controller ใช้ immutable app release เดียวกับ API, cached base imageบนnodeเดิมแบบNeverpull และตรวจ actual imageID. Server dry-runก่อนสร้าง NetworkPolicy/Jobในnamespace `ttb-uat`; Jobไม่มีserviceaccounttoken, รับเฉพาะ runtime DB connection ของ `ttb_app` และ optional recovery-freezeflag ไม่mountbank/game/metrics secretsหรือprivatefiles. Egressอนุญาตเฉพาะ PostgreSQL5432 และ DNS53; ต้องไม่มีpolicyอื่นเลือกJobแล้วเปิดegressเพิ่ม. ก่อนเรียกcommandทดสอบTCPไปprivateAPIว่าถูกปฏิเสธ โดยไม่ส่งHTTPหรือfinancialpayload

Jobรัน `--prepare-uat-test-catalog` แล้วจบ ไม่เปิดHTTP APIหรือworker. Commandใช้transaction/advisorylockสร้างสินค้า/variant/stockledger/auditทั้งชุดพร้อมกัน หากเขียนไม่สำเร็จrollbackทั้งหมด. ชื่อสินค้า/SKU/model bindingที่ชนข้อมูลเดิมถูกปฏิเสธ. Audit marker เก็บ IDs ของชุดที่เตรียมไว้; retryคงราคา สถานะเผยแพร่ สต็อกที่แก้/ใช้ไป และออเดอร์ภายหลัง ไม่มีการเติมสต็อกกลับเป็น20

ObserverตรวจJob UID, imageID, policy UID/spec/owner, API pod/deployment identity, catalogจริงที่APIอ่านได้ และfinancialcountsก่อน/หลัง. รายงานมีเฉพาะpublic productmetadataและaggregatecounts ไม่มีcredentials. Jobที่สำเร็จพร้อมpolicyซึ่งผูกownerกับJobเก็บไว้24ชั่วโมงตามTTLเพื่อoperatorตรวจ. หากJobล้ม เก็บหลักฐานและตรวจสาเหตุก่อนดำเนินการใหม่

## Bank gates และการแก้สินค้า

ขั้นนี้ไม่เปลี่ยน Biller, Merchant, JWT key, callback auth/source flags, report config, shared ingress, KKU หรือ Production. การมีสินค้าUATไม่รับรองการชำระผ่านธนาคารและไม่เปิด simulator. เมื่อการเตรียมสำเร็จ ใช้หน้าจัดการร้านค้าสำหรับแก้สินค้า/ราคา/stockตามสิทธิ์; รายการชำระและสิทธิ์ดิจิทัลเกิดจากpayment evidenceตามระบบปกติเท่านั้น

หลักฐานจะเก็บใต้ `.artifacts/uat-catalog/<run-id>/` และ `/opt/ttb-commerce/uat/catalog-preparation/<run-id>/`. ผลเต็มของรุ่นที่ผ่าน CI/deploy และlive preparationต้องบันทึกหลังตรวจจริง ห้ามใช้syntax/dry-run/Local testsแทนlive acceptance


## ผลตรวจจริง 19 ตุลาคม 2026 เวลา 23:40 น.ไทย

Backend CI `a4d8faea5da34c419182b2eadd544147` ผ่าน **268/268** ไม่มี skip, reader3, 173 source inputs และ 459 Linux artifacts; ตรวจ SHA256 ของ artifacts จริงซ้ำแล้ว. TRX SHA256 `b6cde81009499c4c1a8481d90a300955e8bdfa2efa7aa904b4b5252ad8853a0b`. Private UAT release `20261019-p2-uat-catalog`, archive SHA256 `ddd327ee62bfe6cc25af391fceaef57d82318735f0aed157a8adb39102157dbd`

Live preparation run `7e9ded41651e4aec8b07fff91e31bc67` จบ verified เวลา `2026-10-19T16:39:06.890576Z`; Job UID `2a48903e-efeb-48d9-aa20-c544ef473d62`, NetworkPolicy UID `714fe509-c206-4963-a61c-bfa821357d00`. Public catalog มี3สินค้า/3variants ราคา99/10/1บาท, physical stock20ต่อSKU, stock movements2และcatalog audits9. จำนวน financial/effect ทั้ง15กลุ่มก่อน/หลังเป็น0. ตรวจ actual imageID, API identity, policy/spec/owner และ TCP egress denial ผ่าน; ไม่ mount bank secrets/เปิด simulator/เริ่ม APIหรือworkerจากJob. Completed Jobและowned policyยังเก็บตามTTL86400seconds ไม่อ้างว่าลบแล้ว

หลักฐาน `.artifacts/uat-catalog/7e9ded41651e4aec8b07fff91e31bc67/result.json` และ `postcheck.json`: API generation25/observed25/Ready1, UAT7deploymentsและKKU API Ready1, monitoring3targets Up/32ruleshealthy, backup roleอ่านProductsและTestIssuerProfilesได้. Shared Service Local/RV5847288/31842, transport ConfigMap RV5855110 และ API policy RV5755920 คงเดิม. Payments/reports readinessfalse; public shopper/callback/metricsยังปิด และไม่มี actual bank request
