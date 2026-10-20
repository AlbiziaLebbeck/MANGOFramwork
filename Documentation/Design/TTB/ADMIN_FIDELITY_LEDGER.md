# Admin fidelity and functional evidence — 19 October 2026

The catalogue/orders/finance/reports screens and manual-refund/goods extension have been implemented and reviewed. P7-03 implementation acceptance is complete; private UAT release`20261019-p7-finance` is installed with real payment/policy gates closed. This is not bank acceptance.

## Evidence and viewport

Concepts in this directory: admin-catalog-desktop, admin-orders-desktop, admin-finance-desktop, admin-reports-desktop and admin-finance-mobile. Latest renders in `.artifacts/admin-qa/`: catalog-desktop/mobile, orders-desktop, finance-desktop/mobile, reports-desktop/mobile-unconfigured and reports-desktop/mobile-fixture. Desktop 1536×1024 matches concept dimensions. Mobile is 390×844 CSS pixels; the generated mobile reference was returned as 853×1844 instead of exact2×, so comparisons use logical phone proportions rather than an 853px tablet viewport.

IAB inspected login, the finance queue, a real Local case and its original-time uncertainty, and the operations list. Repeatable browser tests exercise the actual Local API. The populated report screenshots use explicitly synthetic **GET metadata only**; there is no report upload/ingest, bank request or fake successful financial mutation. Unconfigured report screenshots come from the real Local API and show the unavailable service honestly.

Latest admin browser3/3 passed35.5s after focused-mobile changes. The preceding broad browser run passed shopper3/3, owned Void1/1 and admin finance/report2/2; catalogue's text-content locator failed and its corrected workflow passed separately, including the new digital upload. Subsequent admin3/3 validates all final admin changes. No skipped tests. Frontend security16/16 and application/test-harness type checks passed before the final CSS-only mobile adjustment. Backend full193/193 and the later order-case-filter HTTP1/1 passed.

## Comparison and repairs

| Point | Concept evidence | Render evidence / repair |
|---|---|---|
| Shell / copy | Plain MANGOs, four tabs, underline, return-to-store | Finance shell selected consistently across all tabs despite inconsistent generated icon treatments. Runtime values replace every sample count/name/reference. Real unavailable reports show no zero-settlement claim. |
| Layout / container | White canvas, roughly60/40 queue/inspector, vertical rule, table rows | Corrected inherited storefront dialog radius/shadow/width and search-label column layout. Latest desktop uses a full white workspace, 38px gutters, a vertical rule and the finance table's subtle outline. No dashboard card grid or decorative gradients. |
| Typography | Noto Sans Thai, page34px, inspector26px, controls/body16px | Explicit MangoThai font and control sizes, table16px, evidence16px, amount32/34px. Increased undersized evidence/table text after the first comparison; long references wrap. Dates use Thai locale/Bangkok (Buddhist display year), while bank wire timestamps remain Gregorian. |
| Palette / assets | Forest, muted green selection, gray proof surface, amber test status | White/forest palette and selected row retained. No raster screenshots shipped as controls. Catalogue is text/table-led as in the concept; no new image placeholders. All icons use existing lucide outlines. |
| Evidence / reports | Totals table, explicit caution, compact evidence reference | Replaced raw hashes above totals with a readable Credit/Debit/count table, Thai row outcomes and a compact copyable hash. Full hash/JSON remains in an expandable audit section. Passing file totals and row completion never imply daily reconciliation/settlement; unknown original time remains visible. |
| Catalogue variants | Concept combines SKU/price and product fields | Intentional functional adaptation: product metadata and variant/SKU writes have independent versioned API actions. Keep separate save actions, a variant selector and digital delivery/file fields rather than imply one atomic combined save. Stock is read-only until a separate UUID/delta/reason adjustment. This extends the inspector below the first viewport. |
| Mobile | One case, return-to-queue, readable proof and full-width actions | Queue/detail replaces compressed side-by-side columns. Hide tab row on selected detail and restore it on return, preserving phone space. 390px screenshots checked; no horizontal overflow. Long IDs wrap and actions stack. Catalogue/digital fields remain operable by scrolling. |
| State / motion | Deliberate selected rows, real form actions | No simulated progress or fake rows. Fixed a hidden numeric-step constraint that blocked creating a product. Fixed login/cart completion overriding a later user navigation; regression test covers this. Screenshots wait for busy completion, dismiss routine notices and reset the scroll position. |
| Notes / financial actions | Notes plus explicit refund review | Added audited claim/save, current version and stable operation IDs. Refund review reads local evidence only; a separate explicit full-refund confirmation submits the selected credit. Unknown bank outcomes never automatically resend. Timeline height is bounded to keep long case history manageable. |

Copy audit: retained concept headings/labels/actions. Added only contract-required fields, exact evidence states, unavailable/empty states, approved-policy acknowledgement, role-specific actions and clear descriptions of non-primary refunds. These are listed in ADMIN_DESIGN_SPEC.md. Generated product/publication inconsistencies and fictitious bank totals were discarded. Catalogue screenshot uses real seeded Local products and current stock; tests create unpublished QA drafts for mutations, not fabricated runtime rows.

## Functional boundaries checked

- Actual server case/order/catalogue data; CSRF/role checks and no-store owned requests. Same-account role loss erases private admin state and discards pending responses.
- Lost stock and case-note responses retry with the same UUID: one stock delta / one note. Changing input rotates the UUID; the server rejects conflicting reuse.
- Physical pickup preparation→ready→delivered, explicit Mock full refund, confirmed result, a scoped finance case and account switching. No automatic Verify/Enquiry request in this flow.
- Private digital file upload uses a synthetic TXT created inside the workspace; the saved private key belongs to a draft download variant. No customer/bank file was transmitted.
- Complete/staged reports cannot be reprocessed through the UI; a quarantined synthetic inspector enables the reason/action only under its configured fixture. That test never submits a report mutation.

The four screens and finance extension follow the accepted tokens and hierarchy after the recorded functional adaptations. Actual phone/bank certification remain P8.

## Manual/goods extension review

- Desktop1536×1024: full approval form, separate evidence area, exact refund amount, second-officer decision and per-unit goods inputs are readable. Workflow forms were moved before the long generic evidence/timeline so the primary task is reachable. No decorative cards or unrelated metrics were added.
- Mobile390×844: single-column inspector, private proof filename wraps safely, full-width reason/decision controls, explicit zero-default stock quantity and independent digital-unit checkboxes. The case's long Thai heading wraps; there is no horizontal overflow. Long forms scroll under the existing fixed header.
- Latest `manual-approval-desktop`, `manual-submitted-mobile`, `goods-review-desktop/mobile`, `shopper-partial-refund-mobile` screenshots were viewed with `view_image` against the finance concept/spec. The shopper refund block initially inherited a flex message layout and became narrow text columns; it now explicitly uses block layout, and the corrected image was inspected.
- IAB first inspection initially used a previously installed PWA shell; explicit UI updates caught it up, and its document release was checked against the currently served shell before inspecting the real goods-review case. Repeatable fresh Chromium tests cover actual Local APIs and two finance sessions. Browser8/8, security17/17, typed app/harness and production build pass.
- Evidence includes synthetic Local/Mock transfer declarations only. No attachment, bank transfer reference or internal hash appears in the shopper summary. The original purchase receipt remains accessible alongside the approved refund summary.
