# TTB operations interface — P7-03

Concepts generated and inspected on 18 October 2026 before frontend implementation:
`admin-finance-desktop.png`, `admin-orders-desktop.png`, `admin-catalog-desktop.png`, `admin-reports-desktop.png` (1536 × 1024), and `admin-finance-mobile.png` (390 × 844 layout, rendered at 2×).

## Concept prompt inventory

All prompts requested a real Thai merchant operations screen, white background, dark forest green, Noto Sans Thai, MANGOs wordmark, slim amber Local test notice, no charts/marketing/cards, and a table left / inspector right. Finance requested evidence and notes for a duplicate payment with an unknown original credit time; orders requested pickup preparation; catalogue requested product/model/SKU editing and separate stock adjustment; reports requested ISO/LOST versions, validation and an explicit reason for reprocessing. Mobile requested one case with a return-to-queue link, proof, timeline, notes and full-width actions. All names, counts and references drawn in concepts are illustrative and must be replaced with actual server data.

## Layout and tokens

- Shared header follows the finance concept: plain text tabs สินค้า / คำสั่งซื้อ / การเงิน / รายงาน, forest underline for selected tab, MANGOs left, กลับหน้าร้าน right. Header 68px desktop; 24–38px side gutters; max content width 1600px. Icons are lucide 20px, stroke 1.75–2.
- White #fff canvas; text #102c28; muted #687878; forest #19483d; selected #e9f2ed; border #d7dfde; surface #f4f6f6; amber #a95c00 / #fff4dd. No gradients or decorative shadows.
- Noto Sans Thai shared with storefront. Page headings 34px/1.3, inspector headings 26px, body/controls 16px/1.55, labels 14px/1.5, amount 32px bold. Controls explicitly inherit the font; height >=44px, radius10px, textarea >=90px.
- Desktop work area uses 60% queue / 40% inspector with a 1px vertical rule, 24–30px gap. Table uses horizontal rules and a pale header, selected row pale green. Finance table has a subtle outer border and rounded corners; other tables are open. Inspector uses sections and dividers, not a grid of cards.
- At <=850px display queue or one selected detail, never squeeze both columns. Selected detail has คิวเคสการเงิน / กลับรายการ. Mobile queue header wraps tabs on a separate row; a selected detail hides those tabs and keeps its return-to-queue action, matching the focused mobile concept. Long bank identifiers wrap; money and table values never clip. Actions wrap/full width. Keyboard selection is a button per row, focus is visible.

## Allowed visible copy and necessary adaptations (recorded before implementation)

Use concept headings/labels/actions: สินค้า, เพิ่มสินค้า, แก้ไขสินค้า, ชื่อสินค้า, ประเภทสินค้า, รายละเอียด, Model key, SKU, ราคา (บาท), จำนวนพร้อมขาย, ปรับสต็อก, บันทึกการแก้ไข, เผยแพร่สินค้า; คำสั่งซื้อ, ผู้ซื้อ, ยอดชำระ, การส่งมอบ, รายการสินค้า, รวมทั้งหมด, การรับสินค้า, หมายเหตุการจัดเตรียม, เริ่มจัดเตรียม, ดูเอกสาร, ดูเคสการเงิน; เคสการเงิน, รอตรวจสอบ, รอไฟล์รายงาน, ประเภทเคส, ยอดเงิน, สถานะ, ยอดเงินที่เกี่ยวข้อง, หลักฐานรับเงิน, อ้างอิงการรับเงิน, ลำดับเหตุการณ์, เหตุผล / บันทึก, บันทึกหมายเหตุ, ตรวจเงื่อนไขคืนเงิน, ดูคำสั่งซื้อ; รายงานธนาคาร, ชื่อไฟล์, วันที่รายงาน, รุ่น, รายละเอียดรายงาน, การตรวจสอบซ้ำ, เหตุผลที่ตรวจสอบซ้ำ, ส่งตรวจสอบไฟล์เดิม. Search, refresh, pagination and empty/loading/error copy may describe their real state.

Functional adaptations required by the implemented contracts:

1. Concept shell icons/selected-tab backgrounds differ across generated screens; use the plain finance underline shell consistently. Keep storefront and operations navigation separate. Role-filtered tabs are derived from the validated server session; backend checks remain authoritative.
2. No illustrative rows, counts, buyer names, bank filenames, totals, or success states in runtime UI. If reports are disabled show ยังไม่เปิดรับไฟล์ธนาคาร / รอข้อมูล SFTP และการยืนยันรูปแบบไฟล์; waiting count is unavailable, never zero or reconciled. Reprocessing is disabled for staged/completed immutable files.
3. The concept label วันที่รับเงิน becomes เวลาเงินเข้าต้นทาง. Unless ISO evidence proves the original time, show รอยืนยันจากรายงาน; bank system TransDate is not original payment time. Money uses exact minor-unit strings, never a floating point conversion of arbitrary bank amounts.
4. Add รับผิดชอบเคส and บันทึกหมายเหตุ for actual audited case workflow. Notes have the server's 2000-character bound (concept500 is illustrative); void reasons have500. Version conflicts require reloading. An operation UUID persists across an uncertain retry; changed input requires a new operation.
5. ตรวจเงื่อนไขคืนเงิน opens an explicit review of the selected full bank credit; it never sends a refund itself. The final separate ยืนยันคืนเงินเต็มจำนวน action requires reason and eligibility, with confirmation of delivered/used goods only for the primary payment. Unknown outcomes offer ตรวจผลยกเลิก, never an automatic resend. Extra-credit refunds must leave primary purchased rights intact. Cross-border/out-of-window/partial refunds need an external process and evidence; do not imply the bank API supports them.
6. No generic paid-state editor or cash adjustment. Evidence-backed recovery, finance resolution, returns and restocking are distinct audited operations. Do not hide missing bank/company/policy provisioning with fake success.
7. Catalogue shows actual publication status (concept's Draft/Published contradiction is discarded), immutable product kind on edits, read-only stock totals with a separate delta/reason form, public preview metadata separate from private digital asset keys, variants with their own versions. Additional fields are required for the existing API, not decorative content.
8. Orders show actual per-line transitions and version, pickup/shipping details and required tracking fields. Finance documents/cases are hidden from fulfillment-only users. No free choice of a completed status. Private downloadable files use authenticated requests, not exposed storage URLs.
9. Private admin state clears immediately on account or role changes; in-flight results are discarded by identity/role epoch. No cache/storage of finance PII, report plaintext or tokens. No bank polling loop. PWA activation is blocked during privileged mutations just like checkout.

## Verification to complete

Before acceptance compare every concept and corresponding browser screenshot with `view_image`; record copy/layout/type/palette/icons/responsive mismatches and fixes in `ADMIN_FIDELITY_LEDGER.md`. Use IAB first, then repeatable browser tests for actual role-gated workflows, account change, mobile overflow and privileged actions. Build/type/tests alone are not visual acceptance.
### Manual refund and goods review extension

Reuse the existing finance inspector, typography, white surfaces, dividers and forest action buttons. Add an inline external-refund form beside the selected credit, with exact refundable/held/approved totals and a reason. Clearly identify this as recording an external transfer, with no transfer CTA. The case inspector shows private evidence filenames/hash references, transfer declaration and a separate second-officer decision. Approval stays disabled for the maker/submitter or unapproved policy. Goods review uses one quantity input per tracked line and one checkbox per digital unit; full refund automatically includes all remaining units. Restock defaults to zero and must be explicit. Require confirmation of consumed goods and the exact selected decisions. Use the current single-column mobile inspector with full-width controls; no new navigation or metrics. Shopper history shows only approved primary refund totals and returned quantities, with original receipt retained.
