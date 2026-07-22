#if USE_DATA_CACHING
const legacyCacheName = {{{JSON.stringify(COMPANY_NAME + "-" + PRODUCT_NAME + "-" + PRODUCT_VERSION )}}};
const previousCacheName = legacyCacheName + "-static-v2";
const cacheName = legacyCacheName + "-static-v3";
const contentToCache = [
    "Build/{{{ LOADER_FILENAME }}}",
    "Build/{{{ FRAMEWORK_FILENAME }}}",
#if USE_THREADS
    "Build/{{{ WORKER_FILENAME }}}",
#endif
    "Build/{{{ DATA_FILENAME }}}",
    "Build/{{{ CODE_FILENAME }}}",
    "TemplateData/style.css"

];
#endif

self.addEventListener('install', function (e) {
    console.log('[Service Worker] Install');

    e.waitUntil((async function () {
#if USE_DATA_CACHING
      const cache = await caches.open(cacheName);
      console.log('[Service Worker] Caching all: app shell and content');
      await cache.addAll(contentToCache);
#endif
      await self.skipWaiting();
    })());
});

self.addEventListener('activate', function (e) {
    e.waitUntil((async function () {
#if USE_DATA_CACHING
      await Promise.all([
        caches.delete(legacyCacheName),
        caches.delete(previousCacheName)
      ]);
#endif
      await self.clients.claim();
    })());
});

#if USE_DATA_CACHING
self.addEventListener('fetch', function (e) {
    const requestUrl = new URL(e.request.url);
    if (e.request.method !== 'GET' || requestUrl.origin !== self.location.origin) {
      return;
    }

    e.respondWith((async function () {
      const cache = await caches.open(cacheName);
      let response = await cache.match(e.request);
      console.log(`[Service Worker] Fetching resource: ${e.request.url}`);
      if (response) { return response; }

      response = await fetch(e.request);
      if (response.ok && response.type === 'basic') {
        try {
          console.log(`[Service Worker] Caching new resource: ${e.request.url}`);
          await cache.put(e.request, response.clone());
        } catch (error) {
          console.warn('[Service Worker] Cache write failed:', error);
        }
      }
      return response;
    })());
});
#endif
