// Service Worker SODIV Bureau — cache "stale-while-revalidate" pour assets + cache offline pour pages produits
const VERSION = 'sodiv-v1';
const CACHE_STATIC = `${VERSION}-static`;
const CACHE_PAGES  = `${VERSION}-pages`;

const PRECACHE = [
    '/',
    '/Catalogue',
    '/css/site.css',
    '/lib/bootstrap/css/bootstrap.min.css',
    '/lib/fontawesome/css/all.min.css',
    '/images/placeholder.svg',
    '/manifest.webmanifest'
];

self.addEventListener('install', (e) => {
    e.waitUntil(caches.open(CACHE_STATIC).then(c => c.addAll(PRECACHE).catch(() => {})));
    self.skipWaiting();
});

self.addEventListener('activate', (e) => {
    e.waitUntil(
        caches.keys().then(keys => Promise.all(
            keys.filter(k => !k.startsWith(VERSION)).map(k => caches.delete(k))
        ))
    );
    self.clients.claim();
});

self.addEventListener('fetch', (e) => {
    const req = e.request;
    if (req.method !== 'GET') return;

    const url = new URL(req.url);
    // Ne jamais cacher : admin, API, comptes, paniers, commandes
    if (url.pathname.startsWith('/admin') ||
        url.pathname.startsWith('/api/')  ||
        url.pathname.startsWith('/Account') ||
        url.pathname.startsWith('/Panier') ||
        url.pathname.startsWith('/Commande')) return;

    // Assets statiques : cache-first
    if (/\.(?:css|js|png|jpg|jpeg|svg|webp|woff2?|ico)$/i.test(url.pathname)) {
        e.respondWith(
            caches.match(req).then(hit => hit || fetch(req).then(resp => {
                if (resp.ok) caches.open(CACHE_STATIC).then(c => c.put(req, resp.clone()));
                return resp;
            }))
        );
        return;
    }

    // Pages HTML : network-first avec fallback cache (offline)
    if (req.headers.get('accept')?.includes('text/html')) {
        e.respondWith(
            fetch(req).then(resp => {
                if (resp.ok) caches.open(CACHE_PAGES).then(c => c.put(req, resp.clone()));
                return resp;
            }).catch(() => caches.match(req).then(hit => hit || caches.match('/')))
        );
    }
});
