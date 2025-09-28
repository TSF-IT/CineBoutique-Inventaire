import { BrowserRouter, Navigate, useRoutes } from 'react-router-dom'
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

const RouterView = () => {
  const isScanSimMode = import.meta.env.MODE === 'scan-sim' || import.meta.env.VITE_SCAN_SIM === '1'

  const routing = useRoutes([
    { path: '/', element: <HomePage /> },
    {
      path: '/inventory',
      element: <InventoryLayout />,
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
      element: <AdminLayout />,
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
        <RouterView />
      </BrowserRouter>
    </AppErrorBoundary>
  </AppProviders>
)
