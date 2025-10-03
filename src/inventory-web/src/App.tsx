import type { ReactNode } from 'react'
import { BrowserRouter, Navigate, useLocation, useRoutes } from 'react-router-dom'
import { AppProviders } from './app/providers/AppProviders'
import { HomePage } from './app/pages/home/HomePage'
import { InventoryLayout } from './app/pages/inventory/InventoryLayout'
import { InventoryUserStep } from './app/pages/inventory/InventoryUserStep'
import { InventoryCountTypeStep } from './app/pages/inventory/InventoryCountTypeStep'
import { InventoryLocationStep } from './app/pages/inventory/InventoryLocationStep'
import { InventorySessionPage } from './app/pages/inventory/InventorySessionPage'
import { AdminLayout } from './app/pages/admin/AdminLayout'
import { AdminLocationsPage } from './app/pages/admin/AdminLocationsPage'
import { AppErrorBoundary } from './app/components/AppErrorBoundary'
import { ScanSimulationPage } from './app/pages/debug/ScanSimulationPage'
import { LoadingIndicator } from './app/components/LoadingIndicator'
import { SelectShopPage } from './app/pages/select-shop/SelectShopPage'
import { useShop } from '@/state/ShopContext'

const ShopGate = ({ children }: { children: ReactNode }) => {
  const { shop, isLoaded } = useShop()
  const location = useLocation()

  if (!isLoaded) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-slate-50 px-4 py-10 dark:bg-slate-950">
        <LoadingIndicator label="Chargement de votre boutique…" />
      </div>
    )
  }

  if (!shop) {
    return <Navigate to="/select-shop" replace state={{ from: location.pathname }} />
  }

  return <>{children}</>
}

export const AppRoutes = () => {
  const isScanSimMode = import.meta.env.MODE === 'scan-sim' || import.meta.env.VITE_SCAN_SIM === '1'

  const routing = useRoutes([
    { path: '/select-shop', element: <SelectShopPage /> },
    { path: '/', element: <ShopGate><HomePage /></ShopGate> },
    {
      path: '/inventory',
      element: (
        <ShopGate>
          <InventoryLayout />
        </ShopGate>
      ),
      children: [
        { index: true, element: <Navigate to="start" replace /> },
        { path: 'start', element: <InventoryUserStep /> },
        { path: 'location', element: <InventoryLocationStep /> },
        { path: 'count-type', element: <InventoryCountTypeStep /> },
        { path: 'session', element: <InventorySessionPage /> },
      ],
    },
    {
      path: '/admin',
      element: (
        <ShopGate>
          <AdminLayout />
        </ShopGate>
      ),
      children: [
        { index: true, element: <AdminLocationsPage /> },
      ],
    },
    { path: '*', element: <Navigate to="/" replace /> },
  ])

  if (isScanSimMode) {
    return <ScanSimulationPage />
  }

  return routing
}

export const App = () => (
  <AppProviders>
    <AppErrorBoundary>
      {/* Migration React Router v6 → v7 : https://reactrouter.com/upgrading/v6 */}
      <BrowserRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
        <AppRoutes />
      </BrowserRouter>
    </AppErrorBoundary>
  </AppProviders>
)
