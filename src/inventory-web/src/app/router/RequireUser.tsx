import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useShop } from '@/state/ShopContext'
import { loadSelectedUserForShop } from '@/lib/selectedUserStorage'
import type { ReactElement } from 'react'

const extractStoredUserId = (stored: ReturnType<typeof loadSelectedUserForShop>): string | null => {
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
