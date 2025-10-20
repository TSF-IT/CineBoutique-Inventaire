// src/pwa/sw-register.ts
const UPDATE_CHECK_INTERVAL_MS = 60 * 60 * 1000

export async function setupPWA() {
  // seulement en prod, seulement côté client
  if (!import.meta.env.PROD) return
  if (typeof window === 'undefined' || !('serviceWorker' in navigator)) return

  // IMPORTANT: pas de littéral direct => Vite n’essaie pas de le résoudre en dev
  const moduleId = 'virtual:pwa-register' as const
  // eslint-disable-next-line @typescript-eslint/ban-ts-comment
  // @ts-ignore – module virtuel PWA ignoré en dev
  const { registerSW } = await import(/* @vite-ignore */ moduleId)

  const { updateSW } = registerSW({
    immediate: true,
    onNeedRefresh() {
      updateSW(true)
    },
    onOfflineReady() {},
    onRegisteredSW(
      _swUrl: string,
      registration?: ServiceWorkerRegistration | undefined,
    ) {
      if (!registration) return
      window.setInterval(() => {
        void registration.update()
      }, UPDATE_CHECK_INTERVAL_MS)
    },
  })
}
