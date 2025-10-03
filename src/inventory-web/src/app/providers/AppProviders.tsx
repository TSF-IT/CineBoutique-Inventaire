import type { ReactNode } from 'react'
import { InventoryProvider } from '../contexts/InventoryContext'
import { OperatorsProvider } from '../contexts/OperatorsContext'
import { ThemeProvider } from '../../theme/ThemeProvider'
import { ShopProvider } from '@/state/ShopContext'

export const AppProviders = ({ children }: { children: ReactNode }) => (
  <ThemeProvider>
    <ShopProvider>
      <OperatorsProvider>
        <InventoryProvider>{children}</InventoryProvider>
      </OperatorsProvider>
    </ShopProvider>
  </ThemeProvider>
)
