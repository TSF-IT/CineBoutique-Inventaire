export async function setupPWA() {
  // garde-fou: seulement en prod, et seulement côté client
  if (!import.meta.env.PROD) return
  if (typeof window === 'undefined' || !('serviceWorker' in navigator)) return

  // import dynamique du module virtuel — uniquement en prod
  const { registerSW } = await import('virtual:pwa-register')
  registerSW({
    immediate: true,
    onNeedRefresh() {},
    onOfflineReady() {},
  })
}
