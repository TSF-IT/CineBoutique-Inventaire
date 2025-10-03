import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import { App } from './App'
import { initializeTheme } from './app/utils/theme'
import { ShopProvider } from './state/ShopContext'

initializeTheme()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ShopProvider>
      <App />
    </ShopProvider>
  </StrictMode>,
)
