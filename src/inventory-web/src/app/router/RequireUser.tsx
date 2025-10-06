import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useShop } from '@/state/ShopContext'
import { loadSelectedUserForShop } from '@/lib/selectedUserStorage'

export const RequireUser = () => {
  const { shop, isLoaded } = useShop()
  const location = useLocation()

  if (!isLoaded) return null // ou un loader si tu veux

  if (!shop) {
    return <Navigate to="/select-shop" replace state={{ from: location }} />
  }

  const stored = loadSelectedUserForShop(shop.id)
  if (!stored?.userId) {
    return <Navigate to="/select-user" replace state={{ from: location, redirectTo: '/' }} />
  }

  return <Outlet />
}
