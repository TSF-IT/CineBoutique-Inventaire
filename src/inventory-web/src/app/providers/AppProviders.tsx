import type { ReactNode } from 'react'

import { ThemeProvider } from '../../theme/ThemeProvider'
import { InventoryProvider } from '../contexts/InventoryContext'

import { ShopProvider } from '@/state/ShopContext'

export const AppProviders = ({ children }: { children: ReactNode }) => (
  <ThemeProvider>
    <ShopProvider>
      <InventoryProvider>{children}</InventoryProvider>
    </ShopProvider>
  </ThemeProvider>
)
