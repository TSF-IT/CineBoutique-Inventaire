import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import { App } from './App'
import { initializeTheme } from './app/utils/theme'
import { ShopProvider } from './state/ShopContext'

try {
  if (typeof window !== 'undefined' && window.localStorage) {
  }
} catch {
  // Ignorer les erreurs d'accès au stockage (navigation privée, etc.).
}

initializeTheme()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ShopProvider>
      <App />
    </ShopProvider>
  </StrictMode>,
)
