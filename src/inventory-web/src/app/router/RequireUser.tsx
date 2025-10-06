import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useShop } from '@/state/ShopContext'
import { loadSelectedUserForShop } from '@/lib/selectedUserStorage'
import type { ReactElement } from 'react'

const extractStoredUserId = (stored: ReturnType<typeof loadSelectedUserForShop>): string | null => {
  if (!stored || typeof stored !== 'object') {
    return null
  }

  const candidate =
    (typeof (stored as { userId?: unknown }).userId === 'string' ? (stored as { userId: string }).userId : null) ??
    (typeof (stored as { id?: unknown }).id === 'string' ? (stored as { id: string }).id : null)

  return candidate && candidate.trim().length > 0 ? candidate : null
}

export default function RequireUser(): ReactElement | null {
  const { shop, isLoaded } = useShop()
  const loc = useLocation()
  if (!isLoaded) return null
  if (!shop) return <Navigate to="/select-shop" state={{ from: loc }} replace />
  const stored = loadSelectedUserForShop(shop.id)
  const selectedUserId = extractStoredUserId(stored)
  if (!selectedUserId) {
    const redirectTarget = `${loc.pathname}${loc.search}${loc.hash}`
    return <Navigate to="/select-shop" state={{ from: loc, redirectTo: redirectTarget }} replace />
  }
  return <Outlet />
}
