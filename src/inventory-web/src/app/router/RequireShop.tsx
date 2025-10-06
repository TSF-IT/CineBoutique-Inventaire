import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { LoadingIndicator } from '@/app/components/LoadingIndicator'
import { useShop } from '@/state/ShopContext'

export const RequireShop = () => {
  const { shop, isLoaded } = useShop()
  const location = useLocation()

  if (!isLoaded) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-slate-50 px-4 py-10 dark:bg-slate-950">
        <LoadingIndicator label="Chargement de votre boutique…" />
      </div>
    )
  }

  if (!shop) {
    return <Navigate to="/select-shop" replace state={{ from: location }} />
  }

  return <Outlet />
}
