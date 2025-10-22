import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import './styles/util-classes.css'
import { App } from './App'
import { initializeTheme } from './app/utils/theme'
import { ShopProvider } from './state/ShopContext'
if (import.meta.env.PROD) {
  import('./pwa/sw-register').then(m => m.setupPWA())
}

initializeTheme()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ShopProvider>
      <App />
    </ShopProvider>
  </StrictMode>,
)
