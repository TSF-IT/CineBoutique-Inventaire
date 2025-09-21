import type { ReactNode } from 'react'
import { AuthProvider } from '../contexts/AuthContext'
import { InventoryProvider } from '../contexts/InventoryContext'

export const AppProviders = ({ children }: { children: ReactNode }) => (
  <AuthProvider>
    <InventoryProvider>{children}</InventoryProvider>
  </AuthProvider>
)
