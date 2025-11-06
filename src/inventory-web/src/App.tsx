import clsx from 'clsx'
import { Suspense, lazy } from 'react'
import { BrowserRouter, Navigate, Route, Routes, useLocation } from 'react-router-dom'

import { AppErrorBoundary } from './app/components/AppErrorBoundary'
import { LoadingIndicator } from './app/components/LoadingIndicator'
import { AdminLayout } from './app/pages/admin/AdminLayout'
import { ScanSimulationPage } from './app/pages/debug/ScanSimulationPage'
import { HomePage } from './app/pages/home/HomePage'
import { InventoryCountTypeStep } from './app/pages/inventory/InventoryCountTypeStep'
import { InventoryLayout } from './app/pages/inventory/InventoryLayout'
import { InventoryLocationStep } from './app/pages/inventory/InventoryLocationStep'
import { InventorySessionPage } from './app/pages/inventory/InventorySessionPage'
import { ScanCameraPage } from './app/pages/inventory/ScanCameraPage'
import { SelectShopPage } from './app/pages/select-shop/SelectShopPage'
import SelectUserPage from './app/pages/SelectUserPage'
import { AppProviders } from './app/providers/AppProviders'
import { ProductDetailsPage } from './features/products/ProductDetailsPage'
import { ProductScanSearch } from './features/products/ProductScanSearch'

import RequireInventorySession from '@/app/router/RequireInventorySession'
import RequireShop from '@/app/router/RequireShop'
import RequireUser from '@/app/router/RequireUser'
import { loadSelectedUserForShop } from '@/lib/selectedUserStorage'
import { useShop } from '@/state/ShopContext'

const AdminProductsPage = lazy(() =>
  import('./features/admin/AdminProductsPage').then((module) => ({
    default: module.AdminProductsPage,
  })),
)

const AdminLocationsPage = lazy(() =>
  import('./app/pages/admin/AdminLocationsPage').then((module) => ({
    default: module.AdminLocationsPage,
  })),
)

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

  const suspenseFallback = (
    <div className="flex min-h-screen items-center justify-center bg-slate-50 px-4 py-10 dark:bg-slate-950">
      <LoadingIndicator label="Chargement de l’application…" />
    </div>
  )

  return (
    <Suspense fallback={suspenseFallback}>
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
            <Route path="/products/:sku" element={<ProductDetailsPage />} />
            <Route
              path="/scan"
              element={<ProductScanSearch onPick={(sku) => console.log('picked', sku)} />}
            />
            <Route path="/admin" element={<AdminLayout />}>
              <Route index element={<AdminLocationsPage />} />
              <Route path="products" element={<AdminProductsPage />} />
            </Route>
          </Route>
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </Suspense>
  )
}

const AppChrome = () => {
  const location = useLocation()
  const isScanCameraRoute = location.pathname === '/inventory/scan-camera'

  return (
    <div
      className={clsx(
        'relative min-h-screen overflow-x-hidden text-(--cb-text) antialiased transition-colors duration-300',
        isScanCameraRoute ? 'pb-0 sm:pb-0' : 'pb-6 sm:pb-8',
      )}
    >
      <AppRoutes />
    </div>
  )
}

export const App = () => (
  <AppProviders>
    <AppErrorBoundary>
      {/* Migration React Router v6 → v7 : https://reactrouter.com/upgrading/v6 */}
      <BrowserRouter>
        <AppChrome />
      </BrowserRouter>
    </AppErrorBoundary>
  </AppProviders>
)
