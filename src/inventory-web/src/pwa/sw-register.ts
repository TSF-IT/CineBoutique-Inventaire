// src/pwa/sw-register.ts
export async function setupPWA() {
  // seulement en prod, seulement côté client
  if (!import.meta.env.PROD) return
  if (typeof window === 'undefined' || !('serviceWorker' in navigator)) return

  // IMPORTANT: pas de littéral direct => Vite n’essaie pas de le résoudre en dev
  const id = 'virtual:pwa-register'
// eslint-disable-next-line @typescript-eslint/ban-ts-comment
// @ts-ignore – module virtuel PWA ignoré en dev
const { registerSW } = await import(/* @vite-ignore */ id)



  registerSW({
    immediate: true,
    onNeedRefresh() {},
    onOfflineReady() {},
  })
}
