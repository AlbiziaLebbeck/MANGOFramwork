# ผลสำรองและซ้อมกู้คืน TTB UAT

ตรวจเมื่อ 19 ตุลาคม 2026 หลังผู้ใช้อนุญาตเฉพาะ UAT: role อ่านอย่างเดียว `ttb_backup` บนฐาน `ttb_uat`, Secret ของบัญชีนี้, พื้นที่สำรองเข้ารหัส 20 GiB, งานทุก 15 นาทีหลังซ้อมผ่าน และกู้คืนไปฐาน/namespace ใหม่ กุญแจถอดรหัสอยู่บน PC ไม่มีการแก้ KKU หรือ Production

## รอบหลังเพิ่มตารางบริษัท — 19 ตุลาคม 2026 เวลา 23:06–23:19 น. ไทย

| รายการ | ผลล่าสุด |
|---|---|
| Archive UAT หลัง migration | งานตามรอบ `ttb-uat-backup-29873760` สำเร็จ 16:00:08 UTC; ciphertext ที่ PC ตรวจ hash ตรง `ecc0a5663317e13695220a69c26459a2c5c99d318ba3181385cf95ee454382cb` |
| กู้คืน UAT schema ใหม่ | **44 ตาราง / 21 แถว**, 11.318 วินาที; มี `TestIssuerProfiles` และชื่อ/คอลัมน์/แถว/sequence ตรง archive |
| กู้คืน Local ที่มีบริษัทและการซื้อทดสอบ | **44 ตาราง / 4,067 แถว / 127 ไฟล์**, 12.653 วินาที; hash ข้อมูลบริษัทที่บันทึกจริง 1 แถวตรงกัน |
| การแยกแหล่งข้อมูล | ฐานกู้คืนอยู่ namespace ใหม่ทั้งสองรอบ, รับเฉพาะ Unix socket, ปิด egress, ไม่เปิด API/worker และไม่ replay jobs |
| Cleanup | ตรวจ namespace ทั้งสองหายแล้วและ PFX ชั่วคราวเหลือ0; ไม่เปลี่ยนฐาน/ไฟล์ต้นทางหรือ KKU/Production |

หลักฐาน UAT: `.artifacts/ttb-backups/096a73cee9e948de8dee08c6e1c02382/transfer.json` และ `.artifacts/ttb-recovery/drill-c001707550284db7bff4100bd639cfcc/`.

หลักฐาน Local: `.artifacts/ttb-recovery/local-source-0a12972032f94db295c41f5f72669170/` และ `.artifacts/ttb-recovery/drill-5fd16501b8b14803a8539c8e29a203aa/`. Local ciphertext SHA256 `d0aefea2f8639a98fb3c6eec1bbc732f404c3689c5c1e0c3477789f66d1fabd0`.

ทั้งสอง directory มี `restore-drill.json`, `data-comparison.json`, `referenced-files-verification.json`, `cleanup-verification.json`. กุญแจยังอยู่บน PC ภายใต้ CurrentUser DPAPI. ตัวเลขเวลานี้ไม่รับรอง Production RPO/RTO, continuous off-server replication, host-loss/key escrow หรือการกู้ไฟล์/ยอดเงินจริงของ TTB

## ผลรอบก่อนเพิ่มตารางบริษัท (ประวัติ)

| รายการ | ผล |
|---|---|
| สิทธิ์ `ttb_backup` | อ่าน 43 ตารางได้; ตารางที่เขียนได้ 0; ไม่มี superuser/สร้างฐาน/สร้าง role/replication/bypass RLS |
| งานสำรองด้วยมือ | `ttb-uat-backup-drill-20261019063953` สำเร็จ; เข้ารหัสก่อนเขียน archive ลง PVC |
| สำเนาออกจากเซิร์ฟเวอร์ | คัดลอก ciphertext มายัง PC ผ่าน SSH ที่ตรวจ host key และเทียบ SHA256 ตรงกับต้นทาง/บันทึก finalization |
| กู้คืน UAT | 43 ตาราง / 20 แถวตรงกัน รวมชื่อ/ลำดับคอลัมน์ ทุกแถว และ sequence; 10.724 วินาที |
| กู้คืนข้อมูล Local จำลอง | 43 ตาราง / 2,881 แถว / ไฟล์อ้างอิง 90 ไฟล์ตรงกัน; 11.289 วินาที |
| งานตามรอบจริง | `ttb-uat-backup-29873205`: controller นัด 06:45:00 UTC, สำเร็จ 06:45:08 UTC (13:45 น. ไทย) |
| สถานะ CronJob | เปิดทุก 15 นาที, timezone UTC, Forbid งานซ้อน; ไม่ลบ archive อัตโนมัติ |
| ระบบตรวจสถานะล่าสุด | API/backup/TLS เป็น Up ทั้ง3target; โหลด27rules, healthOK; promtoolผ่าน14สถานการณ์ รวมexpiry/preparation/failure |
| ชุดทดสอบล่าสุด | backend231/231ไม่มีข้าม, Python recovery/observer/TLS/acceptance35/35, frontendunit17/17และTypeScript/buildผ่าน |
| เดโม Local หลังตรวจท้ายรอบ | เปิด connection/API ใหม่ด้วย source ล่าสุดหลัง readiness503; กลับมา ready ในโหมด Mock, หน้า5176/5186/5188ตอบ200; UI mixed purchase/PDF/history/owner isolation ผ่าน1/1 (18วินาที) |

ข้อมูล Local เป็นข้อมูลจำลองจาก flow ที่ทดสอบไว้: 85 ออเดอร์, 59 bank transactions จำลอง, 59 allocations, 74 เอกสาร Ready, 59 entitlements, 115 stock movements, 15 Void operations, 9 manual refunds และ 326 jobs การกู้คืนเทียบข้อมูลทุกตารางโดยไม่แสดงแถวข้อมูลออก terminal/chat พร้อมตรวจ hash และขนาดไฟล์ assets/PDF/หลักฐานทั้งหมดที่ฐานอ้างอิง ยังไม่มีรายงาน TTB จริงในชุดนี้ จึงไม่ถือว่าซ้อมกู้คืนรายงานธนาคารจริงแล้ว

ทั้งสอง drill ใช้ namespace ใหม่ที่ปิด ingress/egress และ PostgreSQL รับเฉพาะ Unix socket ไม่มี TCP Service, API, PDF worker หรือ commerce worker จึงไม่มีการ replay งานหรือเรียกธนาคาร ระหว่างซ้อมไม่เขียนฐาน/ไฟล์ต้นทาง และล้างเฉพาะ namespace ที่ตรวจ ownership ตรงกับ run ID เมื่อสำเร็จแล้ว ตรวจภายหลังไม่เหลือ namespace ของ drill หรือ temporary PFX

## หลักฐานที่ทำซ้ำได้

| หลักฐาน | ตำแหน่งใน workspace |
|---|---|
| การคัดลอก UAT ciphertext | `.artifacts/ttb-backups/4324d05271f248b68f91c09213d8bbc3/transfer.json` |
| UAT SQL/file restore | `.artifacts/ttb-recovery/drill-fdb46128462c4a8fb0710f032b142dac/restore-drill.json` |
| เปิดและตรวจงานตามรอบ | directory เดียวกับ UAT drill: `schedule-enabled.json`, `scheduled-backup.json` |
| Populated Local SQL/file restore | `.artifacts/ttb-recovery/drill-5197ec1cc4b74b5283592a1d27ae787c/restore-drill.json` |
| ทุกตาราง/sequence และไฟล์อ้างอิง | แต่ละ drill: `data-comparison.json`, `referenced-files-verification.json` |
| Private monitoring | `.artifacts/monitoring-qa/live-monitoring.json`, `targets-private-uat.png`, `alerts-private-uat.png` |
| Full backend regression | `.artifacts/test-results/commerce_net10.0_20261019134003.trx` |
| Requirement matrix | `Documentation/TTB_REQUIREMENT_MATRIX.md`, `.artifacts/acceptance/requirement-matrix.json` |

UAT encrypted archive SHA256: `c7a7b624670048d6ecf8cc49dea1cc438750dd6a7e4d2506e15f5e371b807951`.

Local encrypted fixture SHA256: `904ea4654b51c0fec58f5ea223aaedb93a00e80181eaf37b08845da03e5d0201`.

Public recovery recipient SHA256: `b9457a957c19c5f9ff0b33860d0285b46742fc9d31887ac32ee737b2e3c33b42`. Private key ใช้ Windows CurrentUser DPAPI อยู่บน PC; ส่งไปเซิร์ฟเวอร์เฉพาะ certificate สาธารณะ

คำสั่งใน `Infrastructure/TTB/uat/`:

1. `pull-backup.ps1 -JobName <completed-job>` ยืนยัน finalization, เจ้าของ PVC/PV, path และ hash ก่อนคัดลอก ciphertext เข้า directory ใหม่บน PC
2. `restore-drill.ps1 -ArchivePath <p7m> -ExpectedSha256 <recorded-hash> -ExpectedEnvironment Uat` ถอดรหัสที่ PC แล้วกู้คืนใน namespace ใหม่ ไม่เขียนทับฐานเดิม
3. `enable-backup.ps1 -RestoreEvidenceDirectory <verified-drill> -TransferDirectory <verified-transfer>` ตรวจหลักฐานตรงกันก่อนเปิด CronJob
4. `new-local-recovery-fixture.ps1` ใช้เฉพาะฐาน Local จำลองและไฟล์ Local ที่มีอยู่ ด้วย owner เดิมของ development; ไม่สร้าง role/grant เพิ่ม แล้วใช้ `restore-drill.ps1 -ExpectedEnvironment Local` แยกซ้อมอีกครั้ง

รอบแรกของ producer ล้มเหลวก่อน dump เพราะการเชื่อมต่อ Service ภายในจาก pod ใหม่ยังไม่พร้อม เพิ่มการรอ private PostgreSQL แบบจำกัดไม่เกิน 20 ครั้ง ห่าง 2 วินาที ก่อนเริ่ม dump แล้วรอบใหม่และรอบ controller ผ่าน ไม่ได้เปลี่ยนนโยบายเครือข่ายร่วมเพื่อข้ามข้อจำกัด ส่วน `pg_restore --list` ของ fixture ต้องรับ stdin ให้ครบลง temporary file ก่อน เพราะคำสั่ง list อาจจบก่อนอ่าน archive ครบ ทำให้ stream ของ kubectl ค้าง; แก้แล้วและทั้งสอง actual restore ผ่าน

## สถานะใช้งานและขอบเขตที่ยังเหลือ

Private UAT ณรอบ restore นี้ใช้ release `20261019-p1-test-issuer`, reader contract3; archiveSHA256 `fae3a9c3881ae9e825db5f0955c60f7db31767a8df5f9a5149940be9a92184a7`. API/workers/PostgreSQL/Prometheus/backup/TLSobserverพร้อม และKKUยังพร้อม1/1. มีเพียงoperator-allowlistedExact`/health/live`TLSpreflight; shopper/callback/metricsไม่มีpublicroute. `paymentsReady:false`, reportsปิด และไม่มีauthenticatedTTBrequestหรือเงินจริงในงานนี้

Local ณรอบ restore นี้ใช้ runtime `.tools/local/runtime-25691019152855-a8135402` และ game service key เดิม ข้อมูลเดิมยังอยู่และไม่มี migration เพิ่ม ชุดทดสอบหลังเปิด connection ใหม่เก็บแยกใน `.artifacts/acceptance/local-restart-results.json` จึงไม่เขียนทับ report ชุด browser10tests ของ requirement matrix. ปิดเฉพาะ SSH forward ชั่วคราว5294ของ monitoring หลังตรวจภาพและmetricsแล้ว; เก็บ Local API/UI และ Unity ของผู้ใช้ไว้

Backup observer อ่านเฉพาะ metadata/เวลา finalization/พื้นที่ใน volume แบบ readonly ไม่เปิด ciphertext, ไม่มี SQL/Kubernetes token และปิด egress ใช้ token ตรวจ metrics แยกจากผู้ซื้อ/finance. ค่า scheduling expected แสดงความคาดหมายที่อนุมัติไว้ ไม่ใช่การอ่าน CronJob ผ่าน Kubernetes API; หากพักงานเพื่อ maintenance ต้องบันทึกและจัดการ alert อย่างชัดเจน

ผลเวลาข้างต้นเป็น drill ชุดเล็ก ไม่รับรอง Production RTO ≤4 ชั่วโมงหรือ off-server RPO ≤15 นาที สำเนาออกนอกเครื่องครั้งนี้คัดลอกด้วยมือ ยังไม่มี continuous off-server replication, นโยบาย retention/พื้นที่ปลายทาง หรือ production key escrow ที่อนุมัติ ไม่มี automatic deletion; เมื่อถึง pool budget หรือพื้นที่ shared filesystem ต่ำกว่า reserve งานจะหยุดอย่างมีข้อผิดพลาด แทนการลบ recovery point

เพิ่มpublic-certificate/verifiedTLSprobeและpinnedbackup-recipientexpiryแล้ว โดยobserverไม่รับprivatekey. HTTPSUATผ่านCA/hostnameและservedleafตรงกับใบที่ออก. ล่าสุด19Oct08:44UTCผู้ใช้อนุญาตและทดสอบsharedServiceLocalแล้ว: ingressเห็นPCpublicpeerจริง/allowlistผ่าน, KKUหน้าและUIassetsตรงbaseline/APIhealthปกติ, nodehealthport31842ตอบ200. คงLocalพร้อมrollbackผูกappliedRV5847288; ไม่rollbackจริงและไม่เปิดcallback. Applicationproxyhandoff/TTBNAT/authและKKUactiveWebSocket/sessionยังไม่รับรอง. รายละเอียดใน`TTB_NT_NETWORK_CHANGE_REQUEST_TH.md`และ`.artifacts/tls-qa/`; งานnetworkนี้ไม่แก้backup/restoreconfiguration.

ช่องทางon-callยังไม่ตั้งค่า; JWT/PGP/backup-keyrotation/escrowและhost-lossยังต้องตรวจ. Rulesในdashboardไม่เท่ากับส่งแจ้งเตือนถึงผู้ดูแลแล้ว. ข้อมูลธนาคาร/sourceIP+authจริง, bankUAT, โทรศัพท์จริง และProductionacceptanceยังเป็นเงื่อนไขแยก

