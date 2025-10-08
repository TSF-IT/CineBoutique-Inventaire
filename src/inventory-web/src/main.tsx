import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import { App } from './App'
import { initializeTheme } from './app/utils/theme'
import { ShopProvider } from './state/ShopContext'
import { enablePwa } from './pwa/sw-register'

initializeTheme()
enablePwa()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ShopProvider>
      <App />
    </ShopProvider>
  </StrictMode>,
)
