// Funk's service worker. Funk runs on the server (Blazor Server), so it can't work offline; this only replaces the
// browser's error page with a branded "no connection" page when a page can't be loaded (e.g. Tailscale is off).
// Everything else goes straight to the network, uncached.
const CACHE = "funk-offline-v1";
const OFFLINE_PAGE = "offline.html";
const OFFLINE_ASSETS = [OFFLINE_PAGE, "fonts/Archivo.woff2", "icon-192.png"];

self.addEventListener("install", event => {
    event.waitUntil(caches.open(CACHE).then(cache => cache.addAll(OFFLINE_ASSETS)).then(() => self.skipWaiting()));
});

self.addEventListener("activate", event => {
    event.waitUntil(
        caches.keys()
            .then(keys => Promise.all(keys.filter(key => key !== CACHE).map(key => caches.delete(key))))
            .then(() => self.clients.claim()));
});

self.addEventListener("fetch", event => {
    const request = event.request;
    if (request.mode === "navigate") {
        event.respondWith(fetch(request).catch(() => caches.match(OFFLINE_PAGE)));
    } else if (request.method === "GET" && OFFLINE_ASSETS.some(asset => request.url.endsWith("/" + asset))) {
        // The offline page's own font and icon, when the network is gone.
        event.respondWith(fetch(request).catch(() => caches.match(request)));
    }
});
