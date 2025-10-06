import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useShop } from '@/state/ShopContext'
import { loadSelectedUserForShop } from '@/lib/selectedUserStorage'
import type { ReactElement } from 'react'

export default function RequireUser(): ReactElement | null {
  const { shop, isLoaded } = useShop()
  const loc = useLocation()
  if (!isLoaded) return null
  if (!shop) return <Navigate to="/select-shop" state={{ from: loc }} replace />
  const stored = loadSelectedUserForShop(shop.id)
  const selectedUserId =
    (stored && 'userId' in stored && typeof stored.userId === 'string' && stored.userId) ||
    (stored && 'id' in stored && typeof stored.id === 'string' && stored.id) ||
    null
  if (!selectedUserId) {
    return <Navigate to="/select-user" state={{ from: loc, redirectTo: '/' }} replace />
  }
  return <Outlet />
}
