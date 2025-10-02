import type { ReactNode } from 'react'
import { InventoryProvider } from '../contexts/InventoryContext'
import { OperatorsProvider } from '../contexts/OperatorsContext'
import { ThemeProvider } from '../../theme/ThemeProvider'

export const AppProviders = ({ children }: { children: ReactNode }) => (
  <ThemeProvider>
    <OperatorsProvider>
      <InventoryProvider>{children}</InventoryProvider>
    </OperatorsProvider>
  </ThemeProvider>
)
