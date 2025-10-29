import { registerSW } from 'virtual:pwa-register'

export type UpdateNotifier = {
  show: () => void
  hide: () => void
  onAccept: (cb: () => void) => void
}

const INACTIVITY_TIMEOUT_MS = 2 * 60 * 1000

export const setupPwa = (ui?: UpdateNotifier) => {
  if (typeof window === 'undefined' || !('serviceWorker' in navigator)) {
    return
  }

  let autoReloadCleanup: (() => void) | undefined
  let updateTriggered = false
  let updateSwCallback: (reloadPage?: boolean) => Promise<void> | void = () => Promise.resolve()

  // Broadcast updates to keep multi-tab sessions aligned.
  let channel: BroadcastChannel | undefined
  try {
    if ('BroadcastChannel' in window) {
      channel = new BroadcastChannel('app-updates')
      channel.onmessage = (event) => {
        if (event?.data === 'reload-now') {
          window.location.reload()
        }
      }
    }
  } catch {
    channel = undefined
  }

  const applyUpdate = () => {
    if (updateTriggered) {
      return
    }

    updateTriggered = true
    autoReloadCleanup?.()
    autoReloadCleanup = undefined
    ui?.hide()

    try {
      channel?.postMessage('reload-now')
    } catch {
      // ignore broadcast errors (e.g. unsupported browsers)
    }

    updateSwCallback(true)
  }

  const startAutoReload = () => {
    // Auto-apply updates when the user is inactive or the tab moves to the background.
    autoReloadCleanup?.()
    autoReloadCleanup = undefined

    if (typeof document === 'undefined') {
      return
    }

    if (document.visibilityState === 'hidden') {
      applyUpdate()
      return
    }

    const activityEvents: (keyof WindowEventMap)[] = [
      'mousemove',
      'keydown',
      'mousedown',
      'touchstart',
      'focus',
    ]

    let inactivityTimer: ReturnType<typeof window.setTimeout> | undefined

    const clearTimer = () => {
      if (inactivityTimer !== undefined) {
        window.clearTimeout(inactivityTimer)
        inactivityTimer = undefined
      }
    }

    const cleanup = () => {
      clearTimer()
      activityEvents.forEach((event) => {
        window.removeEventListener(event, activityHandler)
      })
      document.removeEventListener('visibilitychange', visibilityHandler)
    }

    const schedule = () => {
      clearTimer()
      inactivityTimer = window.setTimeout(() => {
        cleanup()
        applyUpdate()
      }, INACTIVITY_TIMEOUT_MS)
    }

    const activityHandler = () => schedule()

    activityEvents.forEach((event) => {
      window.addEventListener(event, activityHandler, { passive: true })
    })

    const visibilityHandler = () => {
      if (document.visibilityState === 'hidden') {
        cleanup()
        applyUpdate()
      }
    }

    document.addEventListener('visibilitychange', visibilityHandler)
    schedule()

    autoReloadCleanup = cleanup
  }

  const updateSW = registerSW({
    immediate: true,
    onNeedRefresh() {
      updateTriggered = false

      if (ui) {
        ui.show()
        ui.onAccept(() => applyUpdate())
        startAutoReload()
      } else {
        applyUpdate()
      }
    },
    onOfflineReady() {
      // Optionnel: afficher une notification "Disponible hors ligne"
    },
    onRegisteredSW(_swUrl, registration) {
      if (registration) {
        setInterval(() => {
          registration.update().catch(() => undefined)
        }, 30 * 60 * 1000)
      }
    },
  })

  updateSwCallback = updateSW
}
