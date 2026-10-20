# TTB ingress → application proxy handoff

สถานะล่าสุด 19Oct2026: live ingress → application handoff ผ่านแล้ว โดยตั้ง trusted controller `10.42.0.7/32` ใน optional UAT ConfigMap `ttb-transport`. พิสูจน์ socket/effective peer ด้วย nonce-bound HMAC ผ่าน GET health ที่ใช้ system CA/hostname จริงแล้ว; callback public ยังปิด และไม่รับรอง actual TTB NAT/auth หรือ bank UAT. รายการก่อน section ล่าสุดด้านล่างเป็นประวัติก่อนตั้ง trust.

## หลักฐานที่มี

- `.artifacts/tls-qa/shared-network-test-result.json`: PCpeerจริงที่ingress, operatorhealth200, nodeที่ปลอมoperatorheaders403, KKUpublic/APIhealthปกติในขอบเขตที่ตรวจ. SharedServiceLocal/RV5847288; ไม่ใช่การรับรองapplicationproxychain
- `.artifacts/tls-qa/generated-proxy-handoff.json` อ่าน08:56:15UTC: configNginxที่generateจริงสำหรับUATมี2locationsที่ตั้ง`X-Forwarded-For $remote_addr` ทั้งคู่ ไม่ส่งต่อค่าจากclientเป็นbankidentity. controllerReady1. การอ่านconfigอย่างเดียวไม่พิสูจน์CNIenforcement
- `CallbackProxyTests` ใช้ASP.NETforwarded-header middlewareจริงและPostgreSQLdevelopmentฐานสุ่ม: trustedproxyอ่านเพียงrightmostpeerหนึ่งทอด; forgedleftentry/X-Real-IP/ไม่มีXFF/invalidXFFไม่ให้สิทธิ์; proxyนอกCIDRและIPv4-mappedนอกCIDRถูกปฏิเสธก่อนเกิดobservationหรือallocation. Genuinebankpeerผ่านtrustedproxyและmappedtrustedproxyได้ACK0000; callbackซ้ำ0004/หนึ่งallocationและเก็บpeerจริงในevidence. UniversaltrustIPv4/IPv6`/0`ถูกปฏิเสธ
- Callback/proxytarget8/8ไม่มีskipผ่าน1m27s: `.artifacts/test-results/commerce_net10.0_20261019155534.trx`, SHA256`cf957dce09a6dd16fc4bc4fb90aa3bd950cfcb394c06c14e165ac7b06891a7ed`. รวมกับfull243ก่อนหน้านี้ได้247distinctlatestresults; ไม่ใช่full247รอบเดียว. MatrixT27ผูกcasesใหม่แล้วและlocal46/46ยังผ่าน. ไม่มีapplicationcode/schema/runtimeconfigเปลี่ยน จึงไม่publish/deployซ้ำ
- ล่าสุด Backend CI full247/247 ผ่านในรอบเดียว ไม่มีskip: `.artifacts/ci/26dc0eed90c546e090a02d981156286f/backend-tests/ci_net10.0_20261019173133.trx`, SHA256 `3957909845d6b36a7b698d886c0ca74347af668227b8117f7b21ee607b44e39a`. รวม middleware/proxy cases ข้างต้น. การ Build ฉาก BaseWorld ใหม่มี acceptance แยก; ดูสถานะล่าสุดของ matrix/ledger ไม่ใช้จำนวน local cases ในบันทึกเก่ารับรองฉากใหม่
- **Live CNI probe ผ่านแล้ว 10:37:28UTC**: `.artifacts/tls-qa/network-boundary-e4ce251695f24878a0ee068b240edff1.json`. Allowed pod ได้health200/bodyถูกทั้ง APIpod และ Service; denied pod ถูก ConnectionRefused ทั้งสองปลายทาง แม้ส่งforgedbankheaders. Policy UID/RV/specเดิม `ce797d42-d804-4b15-b906-6163f75090ec`/`5755920` ไม่เปลี่ยน. ตรวจJobUID/runlabelและลบเฉพาะJobs/Podsของรอบนี้แล้ว ไม่เหลืองานทดสอบ
- k3s imageที่importไว้รายงาน config digest `sha256:2c1f4e0fd6450f43ddb46d60c2a6df30855a8588e165b1f2559fb0eda8d7ff35` ไม่มีrepositorydigests. Probeใช้cached APIbaseimageบนnodeเดียวกันด้วย **imagePullPolicy Never** แล้วตรวจactualimageIDของทั้งสองงานตรงAPIทุกตัว. ไม่อ้างว่าtagเป็นimmutablepullreference; proofระบุ `immutablePullReferenceUsed:false` และattestedcontentdigest. ไม่มีการpull image/Secret/SA token/privatevolume/SQL/callbackPOST/bankrequest
- Read-only topology หลังprobe: `.artifacts/tls-qa/proxy-topology-after-cni.json`. APIpod `10.42.0.103`, ingresscontrollerReadyที่ `10.42.0.7`/hostNetworkfalse/nodeเดียวกัน. NetworkPolicyในUATทั้งหมดที่อ่านมีเพียง `ttb-api-ingress` เลือก API; อนุญาตcontrollerโดยnamespace+podselectorและTTBpart-of pods ตามspec. APIexplicitTrustedProxyenvยัง0. IPcontrollerนี้เป็นข้อมูลtopology ไม่ใช่หลักฐานว่าapplicationเห็นpeerนั้นแล้ว

## ขั้นตอนก่อนเปิด proxy trust (ประวัติที่ดำเนินการแล้ว ข้อ 5 ยังรอธนาคาร)

1. ยืนยันNetworkPolicy`ttb-uat/ttb-api-ingress`และทุกpolicyที่เลือกAPI; ต้องจำกัดingressจากcontrollernamespace+podselectorและservicesภายในที่ตั้งใจอนุญาต. ตรวจว่าCNIบังคับใช้จริงก่อนตั้งtrustedCIDR
2. `Infrastructure/TTB/uat/verify-api-network-boundary.py --run-id <new32hex>` รันผ่านSSHแล้วตามproofข้างต้น: สร้าง2JobsเฉพาะUAT ใช้cached baseimageAPIบนnodeเดียวกันและตรวจactualcontentdigest/neverpull ไม่มีSecret/volumes/serviceaccounttoken, nonroot/read-only/dropcapabilities, bounded45s/no retry. ต่างกันเฉพาะlabelที่เข้าallowrule. ทั้งคู่GET`/health/live`เท่านั้นไปAPIpodและprivateService โดยส่งforgedbankheaders; allowedต้อง200+bodyถูก และunallowedต้องถูกบล็อกระดับtransportทั้งสองปลายทาง. ไม่มีcallbackPOST/SQL/bankrequest
3. เก็บJobUID/probeRunIDและผลจริงก่อนcleanup. SuccessลบเฉพาะJobsที่ชื่อ/UID/runlabelตรง และตรวจไม่เหลือownedJobs/Pods. Failureหรือobservationtimeoutรักษาidentityเดิมไว้ตรวจ ห้ามตีความtimeoutว่าหยุดแล้วหรือสร้างJobใหม่ทับ. `activeDeadlineSeconds`จำกัดการทำงาน แต่ต้องอ่านterminalstateจริง
4. หลังliveCNIproofผ่าน ค่อยเลือกnarrowcontrollerproxyCIDRตามtopologyที่ตรวจปัจจุบันและขั้นตอนรับIPใหม่เมื่อcontrollerreschedule; ห้าม`/0`หรือเชื่อclientheader. ทดสอบhandoffถึงapplicationจริงแบบhealth-only/controlledfixtureตามวิธีที่ตกลง ก่อนตั้ง`CallbackSourceIpValidated`. ชุดlocalproxytestsไม่แทนlivehandoffหรือTTBNATproof
5. เมื่อTTBยืนยันauth/profile/NATและวงเงินทดสอบแล้ว จึงเปิดexactcallbackrouteและทดสอบauthorized/unauthorizedsource, wrongcredentials/headerchain, durableACK/duplicateตามbankUATpack. ห้ามเปลี่ยนธนาคาร/sourceเป็นsyntheticเพื่อทำgateผ่าน

## รายการที่ยังไม่ได้รันและประวัติ review

ครั้งแรกคำสั่งอ่านAPIimageIDผ่านSSHถูกautomaticapprovalreviewusage-limitก่อนexecute โดยแจ้ง18:35น.; ไม่ใช่คำตัดสินว่าunsafe. หลังreviewอนุมัติงานlocal/SSHdevelopmentใหม่ได้ จึงส่ง **คำสั่งอ่านเดิมผ่านrequire_escalated/approvalreviewตามปกติอีกครั้ง** และได้รับอนุมัติ/executeสำเร็จ จากนั้นตรวจimageformat/CRIและรันprobeผ่านreviewตามลำดับ ไม่ใช้ช่องทางอื่นข้ามreviewและไม่เปลี่ยนsharedServiceซ้ำ. Reviewblockของคำสั่งนี้จึงไม่คงค้าง

ข้อความเดิมก่อน live handoff: ตอนนั้นยังไม่มี application trust หรือ peer proof. ปัจจุบัน application peer/trust ผ่านตาม section ล่าสุด แต่ public callback และ actual TTB NAT/auth ยังไม่ได้รับรอง.

TTBMerchant/issuer/keyregistration/callbackcredentials/SFTP/PGPและบริษัท/device/operationsacceptanceยังค้างตามprovisioningdraftและrequirementmatrix

## ## Live application handoff ที่ผ่านแล้ว

- Release `20261019-p7-transport-proof`, reader 3, archive SHA256 `024904f18e006975de0a954ce9604bcf3640f608c4e415072bc1e8c53d6e072a`. Backend CI 255/255 ไม่มี skip (`ec1c65e34fe04253a8100d0de1be4e02`); transport middleware tests 8 results ครอบคลุม raw/effective peer, auth, nonce, expiry, untrusted forwarding และ bank gates/financial no-write.
- Run `30af4e8287fd40cc84506730f778f4ea` ผ่าน 18:32 น.ไทย: actual operator peer ถึง application, forged bank/multiple-hop headers ไม่เลื่อนสิทธิ์; node ที่ไม่ trusted ยังเห็น socket identity เดิม. Trust อยู่ใน ConfigMap UID `563ee28c-c1e7-4667-b8f7-45c9c96708b4` มีเพียง controller `10.42.0.7/32`; controller UID `f495f753-ae2f-41d8-876f-52de980c9889`. Window ชั่วคราวถูกเอาออกและ valid credential ไม่ได้รับ diagnostic headers แล้ว. Token อยู่ใน memory ไม่ลง args/file/log.
- รอบแรก `c39461ab326b46c8a7d1c322ab4c6634` abort และตรวจ Ready/ไม่มี trust/window/ConfigMap จริงแล้ว เพราะ read-only health observer พบ Endpoint/CNI convergence ชั่วคราวหลัง rollout. แก้เฉพาะ observer ให้รอ GET ที่ Service เดิมแบบ bounded; ไม่ repeat mutation หรือเริ่ม run ใหม่เพราะ observation timeout.
- Postcheck 22:17 น.ไทย: API generation23/observed23/Ready1, 7 UAT deployments และ KKU API/DB Ready1; API policy UID/RV เดิม `ce797d42-d804-4b15-b906-6163f75090ec`/`5755920`; shared Service Local/RV5847288/health port31842 เดิม. Financial aggregates ทั้ง8เป็น0. Private Prometheus 3 targets Up/32 rules healthy. KKU public GET9 URLs เวลา22:22 น. status/body hashes ตรง baseline ทั้งหมด; ไม่ได้ทดสอบ authenticated session/WebSocket.
- หลักฐาน: `.artifacts/tls-qa/transport-30af4e8287fd40cc84506730f778f4ea/`, `.artifacts/tls-qa/transport-postcheck-20261019.json`, `.artifacts/tls-qa/kku-public-after-transport.json`. สิ่งนี้พิสูจน์ operator transport เท่านั้น; `CallbackSourceIpValidated`, actual TTB NAT/auth และ bank profile ยังไม่ผ่าน ไม่เปิด callback จากผลนี้.

### เมื่อ controller เปลี่ยน IP หรือถูกสร้างใหม่

Trust เป็น exact /32 และ NetworkPolicy จำกัด selector ทั้งสองชั้น หาก controller เปลี่ยน IP ระบบไม่เชื่อ forwarding จาก IP ใหม่โดยอัตโนมัติ. ผู้ดูแลต้องตรวจ current UID/IP, NetworkPolicy/CNI และ health-only application handoff ใหม่ ก่อน version-guarded เปลี่ยนเฉพาะ own ConfigMap และ rollout API. ห้ามขยายเป็น Pod CIDR หรือ /0. Script handoff รอบแรกคาดว่า ConfigMap ยังไม่มี จึงไม่ใช่คำสั่ง rotation อัตโนมัติสำหรับ state ปัจจุบัน; ต้องเตรียมและทวน guarded rotation สำหรับ run ใหม่ก่อนรันจริง.
