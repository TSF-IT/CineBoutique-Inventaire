import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import { App } from './App'
import { initializeTheme } from './app/utils/theme'
import { ShopProvider } from './state/ShopContext'
import { enablePwa } from './pwa/sw-register'

initializeTheme()
enablePwa()

async function pingVersion() {
  try {
    const r = await fetch('/version.json?v=' + Date.now(), { cache: 'no-store' })
    const { version } = await r.json()
    const prev = localStorage.getItem('appVersion')
    if (prev && prev !== version) {
      ;(window as any).__HardReloadPWA__?.()
      return
    }
    localStorage.setItem('appVersion', version)
  } catch { /* on laisse vivre si offline */ }
}
pingVersion()


createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ShopProvider>
      <App />
    </ShopProvider>
  </StrictMode>,
)
