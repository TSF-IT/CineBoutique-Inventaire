import type { ReactNode } from 'react'
import { InventoryProvider } from '../contexts/InventoryContext'
import { ThemeProvider } from '../../theme/ThemeProvider'

export const AppProviders = ({ children }: { children: ReactNode }) => (
  <ThemeProvider>
    <InventoryProvider>{children}</InventoryProvider>
  </ThemeProvider>
)
