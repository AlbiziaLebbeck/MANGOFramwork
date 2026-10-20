# MANGOs TTB HTML commerce overlay

React/TypeScript/Vite, mounted into `#mango-ttb-root`. UI controls are HTML; Unity owns the live model/preview camera. The standalone page displays labeled product photography and disables 3D controls until a host attaches. It is a development and QA surface, not a replacement for the metaverse.

Design references and intentional functional adaptations: `Documentation/Design/TTB/DESIGN_SPEC.md`. The four concept screens and three product images were made with built-in ImageGen. Thai font is Noto Sans Thai under OFL (included).

## Local execution

From `Services/TtbCommerce`, run `tools/local.ps1` in a dedicated terminal. It connects only to namespace `ttb-commerce-dev`, creates/reuses `ttb_local_commerce`, migrates and explicitly seeds Local+Mock demo products, then serves API/workers at127.0.0.1:5286. Its hidden SSH tunnel is owned by that process. No UAT/Production/KKU data is seeded or changed.

From this directory: `pnpm install --frozen-lockfile`, `pnpm run build`, `pnpm run preview`. The production preview is127.0.0.1:5176 and proxies `/ttb-commerce/` to that local API. `pnpm run dev` is available for editing; regression tests use production preview to avoid HMR reloads during checkout. The Windows Vite/Rolldown HMR process exited with0xC0000409 in an initial QA run; that is not a passing test. Reports/test-results are excluded from dev file watching. Production build pins React's production environment and contains no dependency on a browser `process` shim.

`pnpm test` runs API/bridge security unit tests. `pnpm run test:browser` runs the repository's Local+Mock E2E regressions; the beforeAll gate rejects any environment that could reach real bank payments. First interactive inspection uses the Codex in-app browser. Playwright tests are the repeatable project CI suite and produce desktop/mobile evidence under `.artifacts/ui-qa`.

## Host contract

The shopper UI has a persistent Thai/English selector (`src/i18n.ts`, `i18n-messages.ts`). It translates presentation text and Bangkok date/THB formatting without reloading or changing buyer inputs, order/quote state, QR payloads, user-authored content or issued PDFs. The root React component subscribes to the locale; stored notices are translated at render time.

Startup and native login notifications now resolve an existing commerce session and automatically exchange an existing MANGOs HttpOnly cookie when necessary. The backend still verifies `/users/me`; no identity is imported from Unity. Checks share one in-flight request, limit anonymous exchanges, honor explicit signout and discard stale account responses. Local/Mock test identities remain explicit. See `Documentation/TTB_SSO_AND_LANGUAGE_TH.md`; `pnpm exec playwright test --config playwright.session-i18n.config.ts` verifies the fixture SSO flow and real Local/Mock language/purchase/receipt flow. Set `TTB_UI_EVIDENCE` for screenshots/reports outside the repository.

Publish the complete `dist` directory together (JS/CSS/products/fonts/license). A host supplies public `window.MANGOS_TTB_CONFIG` before the script: `apiBase` is one of the exact configured TTB API hosts/prefixes or same-origin; `assetBase` points to the published overlay directory. No bank/game secrets or shopper identity belong here. Call `window.MangoTtb.attachUnity(unityInstance)` after Unity initialization. The Unity receiver object is `MangoTtbCommerceBridge`, method `OnBrowserMessage`. Messages are version1.

- Unity→HTML: host.ready, product.open(productId or modelKey), preview.ready/error(requestId), session.changed/end. These never carry authoritative Paid/role flags.
- HTML→Unity: overlay.state, product.inspect, preview.viewport/orbit/zoom/reset/variant/close, session.login, inventory.use(entitlementId). Monetary data is validated by backend; a game server must independently authenticate the connection and entitlement before granting use.
- Use explicit productId when multiple products share one model. `/catalog/by-model/{key}` resolves globally among published products and rejects ambiguous bindings rather than opening the wrong SKU.

Session token and CSRF stay in cookies/memory. Account changes abort old owned requests and discard their responses. Only shopper-scoped operation/order IDs enter sessionStorage. Checkout persists a stable idempotency key before POST; an ambiguous network failure must retry that operation or recover it before a different quote. Paid, fulfillment and document readiness remain separate. Visible GET order/activity polling calls our database only; manual Verify/Enquiry alone consumes the bank budget.

P6 native Unity integration, the trusted FishNet ticket/lease handshake, safe PWA updates and emulated mobile QA are implemented and tested. See Documentation/TTB_UNITY_COMMERCE_CONTRACT.md and Documentation/TTB_PWA_UPDATE_POLICY.md for evidence and boundaries. Actual-device, real-account, bank UAT and production acceptance remain separate.


PWA is explicit via public pwaEnabled:true and a packaged root. The worker caches only hash-verified public shell files; it does not intercept APIs, documents, native runtime or mutations. Updates require a user action and do not reload another tab's checkout. Run node --test ../Tools/TTB/tests/pwa-policy.mjs, playwright.pwa.config.ts, playwright.unity.config.ts and playwright.world.config.ts for the separate policy/browser/native suites. Tools/TTB/update-local-overlay.ps1 refreshes Local frontend/PWA output with the tested native binaries retained.
