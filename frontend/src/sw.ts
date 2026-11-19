/// <reference lib="webworker" />
import { precacheAndRoute, cleanupOutdatedCaches, createHandlerBoundToURL } from 'workbox-precaching'
import { NavigationRoute, registerRoute } from 'workbox-routing'

declare const self: ServiceWorkerGlobalScope

cleanupOutdatedCaches()
precacheAndRoute(self.__WB_MANIFEST)
// App shell offline: toda navegación cae a index.html (excepto rutas de auth/api)
registerRoute(new NavigationRoute(createHandlerBoundToURL('/index.html'), { denylist: [/^\/api\//, /^\/health/] }))

self.addEventListener('message', (e) => {
  if (e.data?.type === 'SKIP_WAITING') void self.skipWaiting()
})
self.addEventListener('activate', (e) => e.waitUntil(self.clients.claim()))
