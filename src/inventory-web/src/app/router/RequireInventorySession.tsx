import type { ReactElement } from 'react'
import { Navigate, Outlet, useLocation } from 'react-router-dom'

import { useInventory } from '@/app/contexts/InventoryContext'

export default function RequireInventorySession(): ReactElement {
  const routeLocation = useLocation()
  const { selectedUser, location, countType } = useInventory()

  if (!selectedUser) {
    return <Navigate to="/select-shop" replace state={{ from: routeLocation }} />
  }

  if (!location) {
    return <Navigate to="/inventory/location" replace />
  }

  if (!countType) {
    return <Navigate to="/inventory/count-type" replace />
  }

  return <Outlet />
}
