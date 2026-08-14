#if USE_DATA_CACHING
const legacyCacheName = {{{JSON.stringify(COMPANY_NAME + "-" + PRODUCT_NAME + "-" + PRODUCT_VERSION )}}};
const previousCacheNames = [
    legacyCacheName,
    legacyCacheName + "-static-v2",
    legacyCacheName + "-static-v3",
    legacyCacheName + "-static-v4"
];
const cacheName = legacyCacheName + "-static-v5";
const contentToCache = [
    "TemplateData/style.css",
    "VoiceCommunication/socket.io-4.7.5.min.js",
    "VoiceCommunication/VoiceCommunication.js"
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
      await Promise.all(previousCacheNames.map(function (name) {
        return caches.delete(name);
      }));
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
      console.log(`[Service Worker] Fetching resource: ${e.request.url}`);
      const useNetworkFirst = e.request.mode === 'navigate' || requestUrl.pathname.includes('/Build/');

      if (!useNetworkFirst) {
        const cachedResponse = await cache.match(e.request);
        if (cachedResponse) { return cachedResponse; }
      }

      try {
        const response = await fetch(e.request);
        if (response.ok && response.type === 'basic') {
          try {
            console.log(`[Service Worker] Caching new resource: ${e.request.url}`);
            await cache.put(e.request, response.clone());
          } catch (error) {
            console.warn('[Service Worker] Cache write failed:', error);
          }
        }
        return response;
      } catch (error) {
        const cachedResponse = await cache.match(e.request);
        if (cachedResponse) {
          return cachedResponse;
        }

        throw error;
      }
    })());
});
#endif
