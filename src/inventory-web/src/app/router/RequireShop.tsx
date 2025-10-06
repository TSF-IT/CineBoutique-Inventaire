import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useShop } from '@/state/ShopContext'

export default function RequireShop() {
  const { shop, isLoaded } = useShop()
  const location = useLocation()
  if (!isLoaded) return null
  if (!shop) return <Navigate to="/select-shop" replace state={{ from: location }} />
  return <Outlet />
}
