import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'

import './index.css'
import './styles/util-classes.css'
import { App } from './App'
import { initializeTheme } from './app/utils/theme'
import { setupPwa } from './pwa/setupPwa'
import { createUpdateToast } from './pwa/UpdateToast'
import { ShopProvider } from './state/ShopContext'

const [UpdateToast, updateNotifier] = createUpdateToast()

if (import.meta.env.PROD) {
  setupPwa(updateNotifier)
}

initializeTheme()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ShopProvider>
      <App />
      <UpdateToast />
    </ShopProvider>
  </StrictMode>,
)
