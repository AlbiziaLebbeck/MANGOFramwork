# บันทึกทดสอบแก้ source IP ของ TTB UAT

สถานะล่าสุด19Oct2026 08:47UTC: ผู้ใช้อนุญาตว่า **“อนุญาตทดสอบเปลี่ยนตอนนี้ ตรวจ KKU และ rollback หากผิดปกติ”** หลังได้รับข้อมูลผลต่อ IP/session ของ KKU และ healthCheckNodePort. เปลี่ยน shared Service จากClusterเป็นLocalเมื่อ08:44:08UTCแล้ว; ทดสอบ source peer/allowlist ผ่าน และ KKU ที่ตรวจยังปกติ จึงคงLocalไว้. ไม่ได้แก้ controller deployment, KKU deployment/route, firewall หรือ Production resources และไม่ได้ส่งข้อความให้ NT/ธนาคาร

## ผลทดสอบจริงตามคำอนุญาต

| รายการ | ผล |
|---|---|
| Service | `mangos-ingress/ingress-nginx-controller`, UID`1cca9e12-7496-4fa3-94c5-01f6b5778bb2` |
| การเปลี่ยน | resourceVersion999/Cluster → **5847288/Local** ด้วย JSONPatch testUID/version/policy; ส่งmutationครั้งเดียว |
| Spec diff | `externalTrafficPolicy` และพอร์ตที่APIจัดให้จริง`healthCheckNodePort:31842`; ไม่ใช้เลขdry-run32769 |
| Topology/health | nodeReady1/localendpointReady1; healthCheckNodePortของnodeตอบ200 |
| PC→TTB | systemCA/hostnameผ่าน; health200; shopper/callback/metrics404 |
| การปลอม headers | operatorปลอมbankheadersยัง200เพราะpeeroperatorมีสิทธิ์เดิม; nodeที่ไม่มีสิทธิ์ปลอมoperatorheadersได้403 |
| Access log | run`78fb937de4944c2280b23f912e53e47c`: 5คำขอจากPCมีpeerตรงSSHoperatorจริง/ไม่ใช่CNIbridge; อีก1คำขอจากnodeมีpeerอื่นและถูกปฏิเสธ |
| KKU | PCหน้าsystemtester redirect308→200; หน้า+7ไฟล์JS/CSSรวม9คำขอให้status/hashตรงbaselineทุกตัว; API`/health`ภายในตอบ`ok/provider:Kku/demo:false`, deploymentReady1 |
| TTB/monitoring | API/workers/DB/observersพร้อม; collector3targetsUp/32ruleshealthyและinactive; paymentsReady:false/reportsปิด |
| Rollback | เก็บpatchผูกUID/Local/appliedRV5847288; serverdry-runคืนspecก่อนเปลี่ยนทั้งหมดและล้างhealthportได้; ยังไม่ได้rollbackจริงเพราะไม่พบอาการผิดปกติในขอบเขตที่ตรวจ |

หลักฐาน`.artifacts/tls-qa/`: `shared-network-before.json`, `shared-network-applied.json`, `shared-network-after-readiness.json`, `shared-network-app-health.json`, `kku-public-before.json`, `kku-public-after.json`, `health-preflight-before.json`, `health-preflight-verification.json`, `ingress-peer-before.json`, `ingress-peer-after.json`, `shared-network-confirmed-rollback.json`. เก็บServiceก่อน/ผลapply/rollbackผูกเวอร์ชันบนNTที่`/opt/ttb-commerce/network-change/ee69b9c7bf0b48d1a575cb3f170815b3`ด้วย

ขอบเขตการรับรอง: พิสูจน์externalpeerถึงingressและoperatorallowlistเท่านั้น. ยังไม่ได้ทดสอบบัญชีKKUจริงหรือactiveWebSocket/sessionเดิม, TTBNATpeerจริง, callbackauth หรือ trusted-proxy handoff ถึง application. UATAPIยังไม่มีexplicitTrustedProxyCidrsในenvironment; ห้ามถือว่าingresspassเท่ากับcallbackpass. Publicshopper/callback/metricsยังปิดและไม่มีคำขอการเงินไปธนาคาร. ผลนี้ไม่รับรองbankUAT/Production

หมายเหตุbaseline: URLไม่มีslashมีredirect308ตามปกติ. การลองurllibจากserverครั้งแรก403และstaticfetchครั้งแรกtimeoutเกิดก่อนapply; baselineสุดท้ายจากPCผ่านครบ9คำขอและserverที่ใช้UAเดียวกับprobeตอบ200ก่อนเปลี่ยนแล้ว. ไม่ใช้ความต่างระหว่างorigin/UAเป็นผลกระทบจากการเปลี่ยน

## ปัญหาก่อนเปลี่ยนที่พิสูจน์แล้ว

- `ttb-api-test.mangosgo.com` ชี้ `209.15.122.115` ตามที่ผู้ใช้ระบุ
- เตรียม Certificate `ttb-uat/ttb-api-test` ด้วย DNS01 issuer เดิมแล้ว มีผลถึง `2027-01-17T06:08:52Z`, กำหนดต่ออายุ `2026-12-18T06:08:52Z`
- ผูก TLS กับ Ingress `ttb-uat/ttb-uat-tls-check` เพียง path Exact `/health/live` และ allowlist เฉพาะ public IPv4 ของผู้ทดสอบที่ sshd เห็นจริง ไม่มี shopper/callback/metrics route
- การเชื่อมต่อจาก PC ผ่าน system CA และ hostname validation แล้ว แต่ทุกคำขอ preflight ตอบ403 รวม health ของ IP ที่ควรอนุญาต
- ตรวจเฉพาะ access log ที่มี correlation `037a0dda527e448d854f039bac8b3299`: ทั้ง6requestsมี peer เป็น CNI bridge ของ node ไม่มีรายการใดตรงกับ public IPv4 ของผู้ทดสอบ บันทึกเฉพาะ hash/boolean ไม่เผย raw IP หรือ headers
- ก่อนเปลี่ยน Shared Service `mangos-ingress/ingress-nginx-controller` ใช้ `externalTrafficPolicy: Cluster`; มี node1เครื่องและ controller pod Ready บน nodeนั้น. ผลเดิมสอดคล้องกับการ SNAT ก่อนถึง ingress; ผลtrafficจริงหลังLocalอยู่ในตารางล่าสุดด้านบน

หลักฐานก่อนเปลี่ยนล่าสุด: `.artifacts/tls-qa/health-preflight-before.json`, `ingress-peer-before.json`; `ingress-peer-observation.json`เป็นrunก่อนหน้า. Bank readiness ยังคง false. ตอนClusterการปลอม X-Forwarded-For/X-Real-IP ไม่ได้รับสิทธิ์เพิ่มเติม แต่ยังรับรองallowlistไม่ได้ เพราะผู้เรียกที่ควรอนุญาตก็ถูกปฏิเสธ

## ข้อเสนอเดิมและการเตรียมที่ใช้ก่อนเปลี่ยน

ตัวเลือกหนึ่งคือเปลี่ยนเฉพาะ `spec.externalTrafficPolicy` ของ Service เดิมเป็น `Local` เพื่อรักษา source address เมื่อ nodeปลายทางมี local ingress endpoint พร้อม. ข้อมูล topology ตอนนี้เป็นเพียง snapshot; ต้องตรวจ routing/load balancer, firewall, health checks และผลต่อ KKU ทั้ง HTTP/WebSocket/session/rate limits ก่อนอนุญาต. หากไม่เหมาะ ให้ NTเสนอทางเข้า TTB แยกที่รักษา source IP ได้ โดยใช้ hostname/port443/IPที่ธนาคารอนุมัติ ไม่ขยาย allowlist ไปยัง CNI bridge และไม่เชื่อ forwarded header จาก client

Patch ที่เสนอ: `Infrastructure/TTB/network-change/external-traffic-local.proposed.json`.

Rollback: `Infrastructure/TTB/network-change/external-traffic-cluster.rollback.json`.

ไฟล์ทั้งสองเป็น **JSON Patch (`--type=json`)** ไม่ใช่ merge patch. ForwardทดสอบUID, resourceVersion999 และpolicyClusterในคำขอเดียวก่อนreplaceexternalTrafficPolicyเป็นLocal; หากServiceถูกสร้างใหม่/แก้ระหว่างทาง คำขอต้องล้มเหลว. RollbackทดสอบUID, policyLocal และ`__APPLIED_RESOURCE_VERSION__` ซึ่งต้องแทนด้วยเวอร์ชันจากผลการเปลี่ยนจริงที่ยืนยันแล้วก่อนใช้. ห้ามใช้ไฟล์templateเป็นrollbackแบบไม่ตรวจstate

แพตช์ตั้งใจแก้เพียงexternalTrafficPolicy แต่ผลdefaultingของAPIเพิ่มhealthCheckNodePortด้วย จึงตรวจspecdiffจากserverdry-runก่อน. `run-authorized-test.py`เป็นone-offrunnerของคำอนุญาตครั้งนี้: applyยึดbeforeRV999และไม่ยอมapplyซ้ำ; rollbackอ่านผลapplyที่บันทึกจริง. ไม่ใช่ขั้นตอนdeployTTBปกติ. หากstate/RVเปลี่ยนต้องตรวจใหม่ ห้ามforcepatchให้ข้ามguard

## ผลdry-runก่อนเปลี่ยน19Oct08:31UTC

`verify-dry-run.py` มีเฉพาะคำขอ`--dry-run=server`ไปยังServiceชื่อที่กำหนดตายตัว ไม่มีโหมดapply. ผลตรวจปัจจุบัน: validforwardผ่าน, UIDผิด/เวอร์ชันเก่า/policyก่อนเปลี่ยนผิดถูกปฏิเสธ, rollbackที่ยังไม่แทนเวอร์ชันหรือpolicyจริงยังClusterถูกปฏิเสธ, และforward+rollbackในvirtualdocumentเดียวผ่าน. UID/resourceVersion999/specจริงทั้งหมดไม่เปลี่ยน. หลักฐาน`.artifacts/tls-qa/shared-network-guarded-dry-run.json`; ผลroundtripนี้ไม่ใช่การซ้อมrollbacknetworkจริง

**พบspecdiffสองfield: externalTrafficPolicy และ healthCheckNodePort.** Serviceเป็นLoadBalancer; เมื่อเปลี่ยนLocalโดยไม่ระบุพอร์ตhealthcheck Kubernetesเตรียมค่าพอร์ตให้อัตโนมัติ. Dry-runรอบนี้คืน32769 แต่ generatedportsอาจต่างเมื่อเปลี่ยนจริง จึงห้ามใช้เลขนี้เป็นแผนเปิดfirewallหรือหลักฐานว่าจองพอร์ตแล้ว. NTต้องประเมินNodePortrange, load-balancerhealthchecks, firewall และผลต่อKKUด้วย. ไม่มีการเปลี่ยนService/เปิดfirewallจริงในขั้นนี้. อ้างอิง [Service API](https://kubernetes.io/docs/reference/kubernetes-api/core/service-v1/), [dry-runและgeneratedvalues](https://kubernetes.io/docs/reference/using-api/api-concepts/#dry-run)

เงื่อนไขtestทำให้การเปลี่ยนขึ้นกับstateเดิมอย่างชัดเจนตาม[JSON Patch test](https://www.rfc-editor.org/rfc/rfc6902#section-4.6) และ[Kubernetes conditional patch](https://kubernetes.io/docs/reference/using-api/api-concepts/#updates-to-existing-resources). หลังlivechangeต้องเก็บresponse/resourceVersion/พอร์ตจริงและตรวจผลภายนอก; dry-runไม่พิสูจน์sourceIPหรือความต่อเนื่องของsession

## Checklist ที่ใช้ก่อนเปลี่ยนครั้งนี้

1. ผู้ใช้/ผู้รับผิดชอบระบบอนุมัติขอบเขตและช่วง maintenance สำหรับ shared ingress ที่ KKU ใช้อยู่ รวมผู้ดำเนินการ วิธีประเมิน active sessions/WebSocket และผู้ตัดสินใจ rollback; ให้ NTร่วมตรวจตามขอบเขตการดูแลที่ตกลงไว้
2. เก็บ Service UID/resourceVersion/spec เดิมและตรวจว่า policyยังเป็นCluster; endpoints/controller/nodeต้องพร้อม. หากstateเปลี่ยนต้องจัดทำproposalและตรวจใหม่ ไม่แก้expectedversionเพื่อข้ามการเปลี่ยนของผู้ดูแลอื่น. คำขอจริงต้องใช้JSONPatchพร้อมtestเงื่อนไข ไม่ใช้mergepatchเดิม
3. เก็บ baseline ของ KKU หน้าsystemtester และการเชื่อมต่อ/sessionที่ผู้ดูแลอนุญาตให้ใช้ทดสอบ โดยไม่ซื้อ/ชำระเงินจริงผ่าน KKU
4. ตรวจserver-side dry-runของpatchก่อน รวมspecdiff/healthCheckNodePortและการไม่persist. เตรียมrollbacktemplateพร้อมขั้นตอนใส่appliedresourceVersionที่ยืนยันแล้ว. หากคำขอจริงtimeout/ผลไม่แน่ชัด ต้องตรวจstateและประสานผู้ดูแลก่อน ไม่ส่งซ้ำ/rollbackแบบเดา

## เกณฑ์ตรวจและ rollback หลังเปลี่ยน

1. รัน `uat/verify-tls-health-check.ps1` จาก PC: CA/hostnameผ่าน, healthจากpeerที่อนุญาต200, headersปลอมไม่เปลี่ยนสิทธิ์, peerอื่นที่ปลอมheaderเป็นoperatorต้อง403
2. รัน `uat/observe-ingress-peer.py --probe-run-id <new-run>` ผ่านSSH: logตรงrunต้องเห็น public peerตามจริง ไม่ใช่ CNI bridge; เก็บเฉพาะผลเทียบ/hash
3. ตรวจKKUbaselineและsessions/WebSocketตามที่อนุญาต พร้อมUATreadiness/backup/TLSmetricsและhealthcheckportจริง. หากผิดปกติหรือsourceIPยังไม่ถูก ให้rollbackภายใต้UID/policy/appliedversionที่ตรงกับchangeนี้ แล้วตรวจซ้ำ. หากversionเปลี่ยนอีกให้หยุดและประสานNT ไม่forceทับstateใหม่. [Service strategyv1.36.2](https://github.com/kubernetes/kubernetes/blob/v1.36.2/pkg/registry/core/service/strategy.go) มีตรรกะล้างhealthCheckNodePortเมื่อไม่ต้องใช้แล้ว แต่ยังต้องตรวจผลจริงหลังrollback ไม่ถือว่าdry-runพิสูจน์การกู้networkสำเร็จ
4. แม้transportผ่าน ก็ยังไม่เปิดcallbackการเงินจริงจนรับรอง bank source/auth/JWT และ UAT profileจริง. ต้องทดสอบ TTBNAT peerจริง รวมการปฏิเสธIP/authปลอมก่อนเปิดบริการ

## ขอบเขตที่ยังไม่รวมในการอนุญาต

คำอนุญาตครอบคลุมการทดสอบServiceครั้งนี้และrollbackหากผิดปกติ. ไม่ครอบคลุมbanktransaction, เปิดcallback/shopper, Production deploymentหรือการแก้KKUเพื่อรองรับผลข้างเคียง. ไม่มีการส่งร่างให้บุคคล/ธนาคาร/NTอัตโนมัติ
