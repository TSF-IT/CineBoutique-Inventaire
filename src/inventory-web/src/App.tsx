import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
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
import { RequireShop } from '@/router/RequireShop'

const BypassSelect = () => {
  const { shop, isLoaded } = useShop()

  if (!isLoaded) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-slate-50 px-4 py-10 dark:bg-slate-950">
        <LoadingIndicator label="Chargement de votre boutique…" />
      </div>
    )
  }

  if (shop) {
    return <Navigate to="/" replace />
  }

  return <SelectShopPage />
}

export const AppRoutes = () => {
  const isScanSimMode = import.meta.env.MODE === 'scan-sim' || import.meta.env.VITE_SCAN_SIM === '1'

  if (isScanSimMode) {
    return <ScanSimulationPage />
  }

  return (
    <Routes>
      <Route path="/select-shop" element={<BypassSelect />} />
      <Route element={<RequireShop />}>
        <Route path="/select-user" element={<InventoryUserStep />} />
        <Route path="/" element={<HomePage />} />
        <Route path="/inventory" element={<InventoryLayout />}>
          <Route index element={<Navigate to="count-type" replace />} />
          <Route path="location" element={<InventoryLocationStep />} />
          <Route path="count-type" element={<InventoryCountTypeStep />} />
          <Route path="session" element={<InventorySessionPage />} />
        </Route>
        <Route path="/admin" element={<AdminLayout />}>
          <Route index element={<AdminLocationsPage />} />
        </Route>
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
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
