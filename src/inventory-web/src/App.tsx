import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { AppProviders } from './app/providers/AppProviders'
import { HomePage } from './app/pages/home/HomePage'
import { InventoryLayout } from './app/pages/inventory/InventoryLayout'
import { InventoryCountTypeStep } from './app/pages/inventory/InventoryCountTypeStep'
import { InventoryLocationStep } from './app/pages/inventory/InventoryLocationStep'
import { InventorySessionPage } from './app/pages/inventory/InventorySessionPage'
import { ScanCameraPage } from './app/pages/inventory/ScanCameraPage'
import { AdminLayout } from './app/pages/admin/AdminLayout'
import { AdminLocationsPage } from './app/pages/admin/AdminLocationsPage'
import { ProductScanSearch } from './features/products/ProductScanSearch'
import { AdminProductsPage } from './features/admin/AdminProductsPage'
import { AppErrorBoundary } from './app/components/AppErrorBoundary'
import { ScanSimulationPage } from './app/pages/debug/ScanSimulationPage'
import { LoadingIndicator } from './app/components/LoadingIndicator'
import { SelectShopPage } from './app/pages/select-shop/SelectShopPage'
import SelectUserPage from './app/pages/SelectUserPage'
import { useShop } from '@/state/ShopContext'
import RequireShop from '@/app/router/RequireShop'
import RequireUser from '@/app/router/RequireUser'
import RequireInventorySession from '@/app/router/RequireInventorySession'
import { loadSelectedUserForShop } from '@/lib/selectedUserStorage'

const BypassSelect = () => {
  const { shop, isLoaded } = useShop()

  const hasSelectedUser = shop
    ? Boolean(
        (() => {
          const stored = loadSelectedUserForShop(shop.id)
          if (!stored) {
            return null
          }

          if ('userId' in stored && typeof stored.userId === 'string') {
            const candidate = stored.userId.trim()
            return candidate.length > 0 ? candidate : null
          }

          if ('id' in stored && typeof stored.id === 'string') {
            const candidate = stored.id.trim()
            return candidate.length > 0 ? candidate : null
          }

          return null
        })(),
      )
    : false

  if (!isLoaded) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-slate-50 px-4 py-10 dark:bg-slate-950">
        <LoadingIndicator label="Chargement de votre boutique…" />
      </div>
    )
  }

  if (shop && hasSelectedUser) {
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
        <Route path="/select-user" element={<SelectUserPage />} />
        <Route path="/inventory/start" element={<Navigate to="/select-shop" replace />} />
        {/* Tout ce qui nécessite un user va ici */}
        <Route element={<RequireUser />}>
          <Route path="/" element={<HomePage />} />
          <Route path="/inventory" element={<InventoryLayout />}>
            <Route index element={<Navigate to="count-type" replace />} />
            <Route path="location" element={<InventoryLocationStep />} />
            <Route path="count-type" element={<InventoryCountTypeStep />} />
            <Route element={<RequireInventorySession />}>
              <Route path="session" element={<InventorySessionPage />} />
              <Route path="scan-camera" element={<ScanCameraPage />} />
            </Route>
          </Route>
          <Route path="/scan" element={<ProductScanSearch onPick={(sku) => console.log('picked', sku)} />} />
        </Route>
      {/* Admin: à toi de voir si ça doit aussi exiger un user */}
        <Route path="/admin" element={<AdminLayout />}>
          <Route index element={<AdminLocationsPage />} />
          <Route path="products" element={<AdminProductsPage />} />
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
