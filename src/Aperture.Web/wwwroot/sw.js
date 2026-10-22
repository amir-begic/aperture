// Aperture's service worker. Aperture runs on the server (Blazor Server), so it can't work offline; this only replaces the
// browser's error page with a branded "no connection" page when a page can't be loaded (e.g. Tailscale is off),
// and shows notifications (Settings → Notifications). Everything else goes straight to the network, uncached.
const CACHE = "aperture-offline-v2";
const OFFLINE_PAGE = "offline.html";
const OFFLINE_ASSETS = [OFFLINE_PAGE, "icon-192.png"];

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
        // The offline page's icon, when the network is gone.
        event.respondWith(fetch(request).catch(() => caches.match(request)));
    }
});

// A notification from Aperture (Features/Notifications/PushService.cs): { title, body, url, tag }.
self.addEventListener("push", event => {
    let message = {};
    try {
        message = event.data?.json() ?? {};
    } catch {
        message = { body: event.data?.text() };
    }
    // Every push has to show a notification (iPhones stop delivering otherwise), so there's always a title.
    event.waitUntil(self.registration.showNotification(message.title || "Aperture", {
        body: message.body || "",
        icon: "icon-192.png",
        tag: message.tag || undefined,
        data: { url: message.url || "" },
    }));
});

// Tapping it opens the page it's about: in Aperture's window if one is open, otherwise in a new one.
self.addEventListener("notificationclick", event => {
    event.notification.close();
    const url = new URL(event.notification.data?.url ?? "", self.registration.scope).href;
    event.waitUntil((async () => {
        const windows = await self.clients.matchAll({ type: "window" });
        for (const client of windows) {
            try {
                await client.focus();
                return await client.navigate(url);
            } catch {
                // Not ours to navigate; try the next one or open a new window.
            }
        }
        return self.clients.openWindow(url);
    })());
});
