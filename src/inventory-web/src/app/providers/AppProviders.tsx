import type { ReactNode } from 'react'
import { AuthProvider } from '../contexts/AuthContext'
import { InventoryProvider } from '../contexts/InventoryContext'
import { ThemeProvider } from '../contexts/ThemeContext'

export const AppProviders = ({ children }: { children: ReactNode }) => (
  <ThemeProvider>
    <AuthProvider>
      <InventoryProvider>{children}</InventoryProvider>
    </AuthProvider>
  </ThemeProvider>
)
