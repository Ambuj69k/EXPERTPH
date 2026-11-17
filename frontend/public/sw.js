// Minimal service worker so the billing screen opens without internet.
// App shell + built assets are cached; API calls are never cached (offline bills go through the IndexedDB queue).
const CACHE = 'pharmaos-shell-v1';

self.addEventListener('install', event => {
  event.waitUntil(caches.open(CACHE).then(c => c.addAll(['/', '/index.html'])).then(() => self.skipWaiting()));
});

self.addEventListener('activate', event => {
  event.waitUntil(caches.keys().then(keys => Promise.all(keys.filter(k => k !== CACHE).map(k => caches.delete(k)))).then(() => self.clients.claim()));
});

self.addEventListener('fetch', event => {
  const url = new URL(event.request.url);
  if (event.request.method !== 'GET' || url.origin !== self.location.origin || url.pathname.startsWith('/api/')) return;
  if (event.request.mode === 'navigate') {
    // network first for pages, fall back to the cached shell (client-side routing handles the path)
    event.respondWith(fetch(event.request).then(res => {
      const copy = res.clone();
      caches.open(CACHE).then(c => c.put('/index.html', copy));
      return res;
    }).catch(() => caches.match('/index.html')));
    return;
  }
  // hashed build assets never change: cache first
  event.respondWith(caches.match(event.request).then(hit => hit || fetch(event.request).then(res => {
    if (res.ok) { const copy = res.clone(); caches.open(CACHE).then(c => c.put(event.request, copy)); }
    return res;
  })));
});
