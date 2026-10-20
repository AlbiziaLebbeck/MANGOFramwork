# TTB component CI/checks

`Infrastructure/TTB/ci/check.ps1` เป็นตัวเรียกแยก Backend/Frontend/Unity ตามแผนข้อ19. ใช้PowerShell7 และเรียกจากrepositoryrootได้ทั้งในเครื่องและrunnerที่เจ้าของจัดเตรียม. ไม่มีSSH/deployment/publicroute/migrationของUAT/Productionในตัวเรียกนี้ และไม่ส่งartifactออกภายนอก

## เรียกใช้งาน

```powershell
& Infrastructure/TTB/ci/check.ps1 -Component Frontend
& Infrastructure/TTB/ci/check.ps1 -Component Backend
& Infrastructure/TTB/ci/check.ps1 -Component Unity
```

แยกเป็น3CIjobs/steps โดยใช้exitcodeเป็นเกณฑ์. ผลอยู่`.artifacts/ci/<unique-run-id>/result.json` และ`inputs.json`; เก็บเอกสารทั้งหมดของrunนั้นเป็นหลักฐาน. Freshhostใช้lockedrestoreตามค่าเริ่มต้น. `-SkipDependencyRestore`ใช้เฉพาะhostที่ติดตั้งdependencies/browserครบแล้ว ไม่ข้ามtestsหรือqualitygate

Backendต้องมี`TTB_TEST_DATABASE`ที่runnerจัดให้ผ่านprivateenvironment: PostgreSQL18 loopbackและadminDBชื่อ`ttb_development`เท่านั้น. ใช้service/databaseที่สร้างเฉพาะCIและroleที่สร้างฐานทดสอบได้ ไม่ใช้TTB/UAT/Production/KKUcredentials. Testsสร้าง/ลบเฉพาะ`ttb_test_<guid>`ของตัวเอง. ต้องเตรียมOSdependenciesของChromiumให้ครบ; defaultrestoreติดตั้งChromiumตามPlaywrightversionของproject. Pipelineไม่สร้างtunnelหรือค้นหาcredentialsบนNTให้เอง และไม่พิมพ์connectionstring

Frontendใช้Node24/pnpmตาม`WebCommerce/package.json`. Unityใช้licensedWindowsUnity6000.3.26f1ที่pathเดิม พร้อมWebGLและWindowsServermodules, Node/pnpm และdependenciesของfrontend. Unitybuildใช้isolated`Builds/TTB/BuildWorkspace`และไม่ปิดEditorที่ผู้ใช้เปิด. อย่ารันgallery/world/CIUnityพร้อมกันเพราะใช้workspace/outputเดียวกัน

## ขอบเขตของแต่ละผล

| Component | ต้องผ่านก่อนสร้างartifact |
|---|---|
| Backend | ไม่รับmissing/wrong/nonloopbackDBหรือfilteredrun; unfilteredunit+PostgreSQLtests; TRXunique/completed/allpassed/noskip พร้อมrequiredmigration/callback/actualChromiumcases; จึงpublishself-containedLinuxAPI+ReceiptProbe, ตรวจreader≥3/freezecontractและhashไฟล์สำคัญ |
| Frontend | app+E2Etypes, unit/security, PWApolicytests, build; sourcehashก่อน–หลังตรงและมีindex/JS/CSSในartifactinventory |
| Unity | isolatedgallery+fullworldServer/Clientbuildsผ่านmarker/errorchecksของตัวเรียกเดิม; protecteduserfiles5รายการไม่เปลี่ยน; hashcompiledoutputs |

ผล`passed`ของCIcomponentไม่ใช่bankUAT/Production acceptance. Browser/compiledWebGLfunctionalacceptanceยังใช้Playwrightconfigsและrequirementmatrixแยกต่างหาก. Backendcross-publishไม่ใช่การยืนยันว่าLinuxreceiptprobeรันผ่าน; ต้องตรวจบนruntimeตามrunbook. UnityCIไม่รับรองscenevisualquality/device/FishNet/receiptโดยอาศัยcompilationอย่างเดียว

ทุกrunผูกsourceinventory/hashกับartifactinventory; หากsourceเปลี่ยนระหว่างBackend/Frontendrunต้องfail. Source/privatepathที่เป็น`.local.json`/PEM/keys/outputs/dependenciesถูกยกเว้นจากinventory. Unityบันทึกprotectedfilehashesและยืนยันหลังbuildเพราะตัวสร้างมีownedgeneratedassetsที่ตั้งใจเปลี่ยน. ไม่มีautomaticdelete/retentionหรือnetworkpublishในตัวเรียก

## หลักฐานปัจจุบัน

- Frontendcomponentรันจริงผ่าน: `.artifacts/ci/ce7abac55dec4e0c8ec8f03fdd12ecd6/result.json`. Typesผ่าน, unit/security17/17, PWA3/3 และbundle377.24KB/gzip107.77KB. Sourceinventory64ไฟล์/compiledartifact13ไฟล์; ตรวจhashกับไฟล์จริงหลังรันอีกครั้ง. ไม่มีfrontendapplicationcodeเปลี่ยน
- Gatevalidation9/9: `.artifacts/ci-gates/517c12361c9a43d88a6087c532f075bf/result.json`. เริ่มจากfull243TRXจริง แล้วพิสูจน์ว่าskipped/missingrow/failedsummary/runnererror/greenunit-onlyreportถูกปฏิเสธ. อีก4กรณีmissingDB/wrongDB/nonloopbackDB/filteredrunถูกปฏิเสธก่อนเรียกcommandใดและไม่เผยsyntheticpassword. ไม่มีSQL/network/bankrequestในการทดสอบgateนี้
- ผลFrontendแรก`939b7e271ae440c6899214bcd9a7879f`ถูกinvalidateหลังauditพบdictionarysortingทำให้inventoryเหลือ1ไฟล์ แม้componentchecksผ่าน. เก็บoriginalreport+hashไว้ และแก้เป็นtypedobjectsพร้อมตรวจrequiredinputs/artifactsก่อนยอมรับ; finalrunด้านบนผ่านแล้ว ไม่ใช้ผลแรกอ้างartifactbinding
- **Backend coordinator รันจริงผ่านแล้ว**: `.artifacts/ci/26dc0eed90c546e090a02d981156286f/result.json`, full247/247 ไม่มีskip (4m42s), reader3/Linux API+ReceiptProbe, 165sourceinputs/459artifacts ตรวจhashตรงกับไฟล์จริงหลังรัน. TRX SHA256 `3957909845d6b36a7b698d886c0ca74347af668227b8117f7b21ee607b44e39a`. ใช้ private development tunnel ที่ได้รับapprovalใหม่; ไม่มีUAT/KKU/Production mutation หรือ bankcall
- Backend CI รอบแรก `1bac17a9cdc34bcc8b3989626dae8131` หยุดก่อนpublishตามจริงเมื่อ HTTP manual-refund test ให้401แทน403: clockจำลองคงที่ทำให้CookieContainerทิ้งcookieที่หมดอายุตามwallclock. แก้เฉพาะHTTPfixtureและเพิ่มauthenticated assertion; banking-cutoff service fixturesยังคงที่. เพิ่ม explicit .NET setter/getter ให้ConnectionStringBuilder เพราะPowerShell dictionary adapterทำให้property assignmentกลายเป็นkeyword. Negativegateล่าสุด9/9 `.artifacts/ci-gates/f74796f6ebab4dcebe8e78ea917b8a09/result.json`; actualBackend247passเป็นpositivecontrolผ่านpreflightและDBจริง
- Frontend coordinator หลังแก้preflight final `.artifacts/ci/ed1c2760057b42f49d8c21bb5520da6e/result.json` ผ่าน types/17unit/3PWA/build; 64inputs/13artifacts ตรวจhashตรงกับไฟล์จริง
- Unity coordinator final **ผ่านแล้ว**: `.artifacts/ci/9653a54b5537465d977d5a772fdc2346/result.json`, Gallery/Server/Client, 264artifacts ตรวจhashจริงตรงและprotected5/5. StrictgateจับmissingFishNetSceneIdในรอบแรก; แก้canonicalRebuildSceneIds/pairedscene+metaSHA และWorldManagerpathเฉพาะcopy. RuntimeQAแยกจากCI: native3/3และworld1/1ผ่านจริง รวมmixedUI/MockQR/pay/history/PDF/lease/logoutและzero page/console errors; ดู `TTB_BASEWORLD_SCENE.md`
- Frontend coordinator หลังเพิ่มworldUI mixed-checkout/receiptและrenderedGuestready checks final `.artifacts/ci/16e37a6f8c3a47c882481947ccaa58e6/result.json` ผ่านtypes/17unit/3PWA/build; source64/artifact13hashตรง. ก่อนหน้านี้เป็นผลตามsource ณ เวลานั้น ไม่ใช้แทนfinalsource
- Frontend final หลังรวม full-world mixed UI checkout/PDF: `.artifacts/ci/16e37a6f8c3a47c882481947ccaa58e6/result.json` ผ่านtypes/17unit/3PWA/build/source64/artifact13 และตรวจhashจริงอีกครั้งใน finalsceneproof. ผลก่อนหน้านี้เป็นประวัติตามsource ณ เวลานั้น
- **ยังไม่มีexternalCIrunner/jobที่ลงทะเบียนหรือรันจริง**. งานP1-05ครอบคลุมcomponentCI/check entrypointsและการรันจริงในเครื่องนี้ตามแผนข้อ19. ฉากBaseWorldใหม่ผ่านLocalแล้ว; fullgoalยังค้างbank/company/device/operationsตามledger. คำสั่งNTimageที่ถูกreviewusage-limitก่อนหน้า retryผ่านapprovalreviewปกติได้รับอนุมัติแล้ว ไม่มีreviewblockคงค้างหรือช่องทางbypass

ทำซ้ำgatevalidationได้ด้วย:

```powershell
& Infrastructure/TTB/ci/test-gates.ps1 -PassingFullTrx .artifacts/test-results/commerce_net10.0_20261019151208.trx
```

TRXนี้ต้องเป็นfullpassที่มีPostgreSQL/callback/actualChromiumcasesจริง ไม่ใช้subsetสีเขียวแทน. ตัวเรียกจะเก็บsyntheticmutationsและผลnegativeแยกจากfinancial/browseracceptance

## Coordinator และ package ชุดล่าสุด 19 ตุลาคม 23:00 น.ไทย

Backend CI `942a591eccee433c84859547e17ddef1` ผ่าน full260/260 ไม่มี skip (4m14s), reader3, 171sourceinputs/459Linuxartifacts ตรง hashก่อน private UAT release `20261019-p1-test-issuer`. เพิ่ม transport observation 8 results และ test issuer/catalog 5 cases. Full TRX SHA256 `3c3940e07156ddb7e0bd380be30ad97a4687782296fef03df36ac18503d1ca93`

Frontend CI `f62e56ebba854719a2b69434e2dda7cb` ผ่าน types ทั้งapp/e2e, 17unit/security, 3PWApolicies และ build. Browser11/11, compiledgallery3/3, fullBaseWorld1/1, PWA1/1 ผ่านไม่มีskip/flaky; matrix46/46และ26requirementsจาก260TRXเดียว โดยbank/Productionยังfalse. หลักฐาน `.artifacts/scene-qa/ui-company-final-20261019/` ผูกผลกับwebpackageปัจจุบัน, 204nativebinaryเดิมจากUnityCI `9653a54b5537465d977d5a772fdc2346` ไม่เปลี่ยน, preparedscene/metaตรงBuildWorkspaceและprotectedUnity5/5เดิม. รอบนี้แก้HTML/CSS/React ไม่อ้างว่าrebuildnativeใหม่หรือผ่านphysical-device/bank acceptance. Records247และcoordinatorรุ่นก่อนด้านบนเป็นประวัติ


## ผลตรวจจริง 19 ตุลาคม 2026 เวลา 23:40 น.ไทย

Backend CI `a4d8faea5da34c419182b2eadd544147` ผ่าน **268/268** ไม่มี skip, reader3, 173 source inputs และ 459 Linux artifacts; ตรวจ SHA256 ของ artifacts จริงซ้ำแล้ว. TRX SHA256 `b6cde81009499c4c1a8481d90a300955e8bdfa2efa7aa904b4b5252ad8853a0b`. Private UAT release `20261019-p2-uat-catalog`, archive SHA256 `ddd327ee62bfe6cc25af391fceaef57d82318735f0aed157a8adb39102157dbd`


ก่อนfullrunนี้ รอบ `cb9a339550dc43b99c3dadcd41586fda` ล้มก่อนpublish:260pass/8fail/0skipจากNpgsql connection reset/refused สอดคล้องSSHerr `client_loop: send disconnect: Connection reset`. เก็บfailedTRXและartifactFilesว่างไว้; ตรวจprocessterminal/privatePGReady/restarts0 และLocaltunnelrecoveryก่อนexplicitfullrunใหม่ ไม่ใช้automaticJobretryหรือปิดหลักฐานล้มเหลว. ชุดใหม่เพิ่ม8resultsของUATcatalog environment/freeze/marker/concurrency/idempotency/rollback/collision; full268เป็นผลที่ใช้matrixปัจจุบัน


## เพิ่มเติม 20 ตุลาคม: UAT Linux/WebGL และ QR optical decode

FrontendCI `25a4092d0fd1402bacf7163c482a7851` ผ่านtypesทั้งapp/e2e,17unit/security,3PWA/build;13artifactsตรวจSHAตรง. TestsourceรวมprivateUATbrowserใหม่และLocalworldconfigแยกtestMatchแล้ว. ไม่มีfrontendapplicationchange; bundleในisolatedUATtemplateตรงCIrawbytes ส่วนcompiledUATJS/CSSมีLFเพิ่มท้าย1byteเท่านั้นและผูกhashทั้งสองชุดใน `.artifacts/uat-world/73b78e1e74064bf58a4b58504bb26e57/source-binding.json`

Paired UAT build `73b78e1e74064bf58a4b58504bb26e57` ผ่านLinuxServer/WebGL/17profilechecks/218artifacts;18Unitysourcesตรงcloneและ9rootprotectedhashตรง. PrivateUATbrowser1/1ผ่าน31.1sโดยไม่mockpayment/identity; บันทึกanonymousMANGOs401ที่คาดไว้3responsesแยกจากunexpectederrors0. Localworld/Gallerycoordinatorเก่าด้านบนเป็นประวัติ ไม่อ้างว่าเป็นnativebinaryชุดใหม่

OpticalQR20/20จากactualBackendreleasePNGviewและCSSจริง: `.artifacts/qr-real-readiness/render-20261020-1/decode-result.json`. ตัวอย่างหมดอายุปี2023/Billerในสเปก,decodePNG+screenshotsด้วยzxing-cpp3.1.1 และตรวจCRCด้วยPythonbinasciiแยกจากC#builder. ไม่แก้backendbusinesssourceหรือเพิ่มการรับรองbankappจากผลoffline
