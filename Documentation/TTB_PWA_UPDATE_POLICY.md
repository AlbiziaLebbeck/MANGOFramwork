# TTB PWA update and offline policy

`pwaEnabled:true` is an explicit opt-in for a packaged TTB public web root. The existing KKU/MANGOsPWA service worker is not reused or modified. `Tools/TTB/package-pwa.mjs` produces a manifest, code-native SVG icon, bootstrap helper, release manifest and root service worker. Standalone builds package automatically. Native build scripts package the completed output; `update-local-overlay.ps1` updates Local frontend/PWA files without changing the native protocol/binaries.

## Cache boundary

Only generated public HTML, the complete React IIFE/CSS, public product images/fonts and install assets enter Cache Storage. Every byte count and SHA256 digest must match the release manifest before installation completes. The worker policy itself participates in the release hash. Repackaging the same output is deterministic. A failed installation removes its new cache and keeps the active version. At most three caches for the same TTB scope are retained; unrelated applications/scopes are untouched.

API requests, session/CSRF data, orders, receipts, inventory downloads, public environment config, external hosts, arbitrary query-bearing requests, POST/PUT/etc., and Unity Build/StreamingAssets are not intercepted by this worker. The sole query exception is an exact `__mangos_release=<this worker's release>` on a manifest-listed public file, normalized to its already-verified cache entry. Additional/token/different-release queries stay network-only. It has no background-sync or financial retry handler. API responses additionally remain `no-store`. Unity/browser internal caching of public runtime files is separate from this small public-shell cache.

Native TTB packaging now compresses data/wasm/framework files to Brotli and gives all four Unity entry assets SHA256 filenames. This uses the Portal static server's existing Content-Encoding/MIME support. `optimize-webgl.mjs` verifies decompressed bytes before publishing new entry paths and removing the old unreferenced files. Unity's own cache policy uses `immutable` only for the content-hashed public `.data.br` file, while bundles keep revalidation and other requests keep no-store. This does not add native files or any private response to the public-shell service worker. A Chromium test runs the actual compiled Unity runtime from the compressed paths and verifies that reload does not transfer the scene data again. WASM can still transfer on reload; full offline native operation is not claimed.

On20October2026 the deployed Portal entry was observed to have hosting analytics injected into `index.html` (3597bytes vs3230built bytes), which prevented the original hash-strict worker from installing updates. Packaging now carries the exact built HTML in `mango-ttb-shell.json`; the worker validates the decoded HTML length/SHA256 before storing it as `text/html`. Transformed network HTML is never accepted as a verified shell. IIFE/CSS/config and worker URLs carry a release identifier to avoid stale CDN aliases. Repackaging remains deterministic. No hosting/WAF/KKU configuration was changed.

Regression: policy4/4, types/unit33/33, ChromiumCDN-update1/1 on isolated loopback5197 passed. The browser test injects HTML and serves old IIFE/worker aliases, checks explicit activation, keeps another tab's release, switches language, loads the canonical public shell offline, and asserts no API mutations/cache entries. This does not claim the fixed build has already been published through the Portal.

The cache contains decoded response bytes. Content-Encoding/Length, transfer encoding and Vary negotiation headers are removed before storing them, while security/content-type headers are preserved. This avoids corrupting a shell fetched through a gzip reverse proxy. The public-shell budget is24MiB; current native shell is approximately6.66MB, excluding Unity runtime files.

## Updates and checkout

- Installing an update does not activate it automatically. The header offers an explicit update action. An active unpaid order, a pending checkout operation or an in-progress action disables it. Cancellation, expiry and payment release the pending-payment gate.
- Applying an update freezes new mutations in that tab before requesting activation. Only that tab reloads on controller change. Another open tab receives a version notification and keeps its current page, QR and pending operation until the user chooses to update there.
- Public navigation and its IIFE remain from the same cached release. The complete IIFE contains its lazy React features; an activated worker cannot replace a partially loaded code chunk mid-checkout. Native runtime/API traffic remains outside the shell cache.
- A second tab can finish its own update while the first tab retains an unpaid QR. The first tab's manual cancellation/payment and API authorization continue to work. No automatic bank Verify/Enquiry call is introduced.
- Checkout recovery retains only shopper-scoped order/operation identifiers in sessionStorage. Receipt numbering, payment/allocation and delivery remain durable backend concerns, independent of the shell version.

## Connection and privacy

Offline reload renders a public shell and a connection message. It does not restore authenticated history or personal data from Cache Storage. A successful API response establishes reachability; a failed network fetch changes the connection indicator. The operating-system `navigator.onLine` flag is a hint, not financial authority and not sufficient grounds to reject a manual recovery request. Explicit retries retain the same operation key. There are no background mutation retries.

The native loader also attempts a real request rather than trusting an OS hint. A load failure offers an explicit connection retry; it does not automatically reload a checkout. Installation uses the browser's own user-gesture prompt when supported; the agent's checks do not install the app on the user's OS.

## Evidence

- Backend regression remains135/135, `commerce_net10.0_20261018160537.trx`.
- Browser security11/11, including cancelled-order update eligibility, no replay after a network failure and mutation freeze during update.
- Node worker/package policy3/3: private/financial/native runtime exclusions, explicit in-scope activation, hash failure isolation, decoded gzip headers and deterministic packaging.
- Chromium manifest parsing/installability reported no errors. Real two-tab update + persisted unpaid QR + cancellation + offline/private-cache test1/1 passed, including a gzip fixture server (11.5s). The complete QR bounds at1280×720 are asserted. `.artifacts/pwa-qa/update-keeps-qr.png` and `offline-shell.png` were viewed.
- Shopper regression3/3 (44.6s): mixed purchase, private real Thai PDF, account isolation, mobile reload and lost-response recovery. Native gallery2/2 (50.8s) with page/console errors forbidden. Original-scene FishNet game-use1/1 (48.0s) after PWA integration, including lease renewal and logout removal. The legacy guest login test observes rendered input/button readiness; it does not assume that a scaled-time debounce completes after a fixed wall-clock sleep.

These are Local+Mock and emulated-device results. Bank UAT, actual mobile-device acceptance, production proxy/HTTPS and real MANGOs-account acceptance remain inP8/P9. The private UAT backend release remains tracked separately from local frontend output.

Technical references: [MDN service worker lifecycle](https://developer.mozilla.org/en-US/docs/Web/API/Service_Worker_API/Using_Service_Workers), [Chrome update lifecycle](https://web.dev/learn/pwa/update), [MDN onLine limitations](https://developer.mozilla.org/en-US/docs/Web/API/Navigator/onLine). These explain why activation/reload and OS connection hints require explicit handling.
