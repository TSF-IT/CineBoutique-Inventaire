import { registerSW } from 'virtual:pwa-register'

function hardReload() {
  navigator.serviceWorker?.getRegistrations().then(rs => Promise.all(rs.map(r => r.unregister())))
    .then(async () => {
      if ('caches' in window) {
        const keys = await caches.keys()
        await Promise.all(keys.map(k => caches.delete(k)))
      }
    })
    .finally(() => location.replace(location.pathname + '?v=' + Date.now()))
}

export const enablePwa = () => {
  const updateSW = registerSW({
    immediate: true,
    onNeedRefresh() {
      updateSW(true)               // auto-update sans demander la lune
    },
    onOfflineReady() { /* optionnel: toast "offline ok" */ },
    onRegisteredSW(swUrl, reg) {
      document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') reg?.update()
      })
    },
  })
  ;(window as any).__HardReloadPWA__ = hardReload
}
