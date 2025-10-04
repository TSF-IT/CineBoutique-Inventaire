import type { ReactNode } from 'react'
import { InventoryProvider } from '../contexts/InventoryContext'
import { ThemeProvider } from '../../theme/ThemeProvider'
import { ShopProvider } from '@/state/ShopContext'

export const AppProviders = ({ children }: { children: ReactNode }) => (
  <ThemeProvider>
    <ShopProvider>
      <InventoryProvider>{children}</InventoryProvider>
    </ShopProvider>
  </ThemeProvider>
)
