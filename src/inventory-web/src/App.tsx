import { BrowserRouter, Navigate, useRoutes } from 'react-router-dom'
import { AppProviders } from './app/providers/AppProviders'
import { HomePage } from './app/pages/home/HomePage'
import { InventoryLayout } from './app/pages/inventory/InventoryLayout'
import { InventoryUserStep } from './app/pages/inventory/InventoryUserStep'
import { InventoryCountTypeStep } from './app/pages/inventory/InventoryCountTypeStep'
import { InventoryLocationStep } from './app/pages/inventory/InventoryLocationStep'
import { InventorySessionPage } from './app/pages/inventory/InventorySessionPage'
import { AdminLayout } from './app/pages/admin/AdminLayout'
import { AdminLoginPage } from './app/pages/admin/AdminLoginPage'
import { AdminLocationsPage } from './app/pages/admin/AdminLocationsPage'
import { useAuth } from './app/contexts/AuthContext'
import { Card } from './app/components/Card'
import { LoadingIndicator } from './app/components/LoadingIndicator'
import { AppErrorBoundary } from './app/components/AppErrorBoundary'

const RouterView = () => {
  const { isAuthenticated, initialising, user } = useAuth()
  const isAdmin = Boolean(user?.roles.includes('Admin'))

  const routing = useRoutes([
    { path: '/', element: <HomePage /> },
    {
      path: '/inventory',
      element: <InventoryLayout />,
      children: [
        { index: true, element: <Navigate to="start" replace /> },
        { path: 'start', element: <InventoryUserStep /> },
        { path: 'count-type', element: <InventoryCountTypeStep /> },
        { path: 'location', element: <InventoryLocationStep /> },
        { path: 'session', element: <InventorySessionPage /> },
      ],
    },
    {
      path: '/admin',
      element: <AdminLayout />,
      children: [
        { index: true, element: isAuthenticated && isAdmin ? <AdminLocationsPage /> : <AdminLoginPage /> },
      ],
    },
    { path: '*', element: <Navigate to="/" replace /> },
  ])

  if (initialising) {
    return (
      <Card className="mx-auto mt-20 max-w-md">
        <LoadingIndicator label="Initialisation de la session" />
      </Card>
    )
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
