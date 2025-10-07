import type { ReactElement } from 'react'
import { Routes, Route, Navigate, useLocation, Outlet } from 'react-router-dom'

// Ces deux-là sont des NAMED exports dans ton repo → import avec accolades
import { HomePage } from '@/app/pages/home/HomePage'
// Celui-ci est un default export dans ton repo → import sans accolades
import SelectShopPage from '@/app/pages/select-shop/SelectShopPage'
import { InventoryLayout } from '@/app/pages/inventory/InventoryLayout'
import { InventoryLocationStep } from '@/app/pages/inventory/InventoryLocationStep'
import { InventoryCountTypeStep } from '@/app/pages/inventory/InventoryCountTypeStep'
import { InventorySessionPage } from '@/app/pages/inventory/InventorySessionPage'
import { ScanCameraPage } from '@/app/pages/inventory/ScanCameraPage'

// Déjà présent dans ton projet (tu l’utilises ailleurs) ; garde-le tel quel
import RequireShop from '@/app/router/RequireShop'

// On s'appuie sur le contexte boutique existant, pas sur un pseudo storage maison
import { useShop } from '@/state/ShopContext'
import { loadSelectedUserForShop } from '@/lib/selectedUserStorage'

type SelectedSnapshot = ReturnType<typeof loadSelectedUserForShop>

function pickSelectedUserId(s: SelectedSnapshot): string | null {
  if (!s) return null
  if ('userId' in s && typeof s.userId === 'string' && s.userId.length > 0) return s.userId
  if ('id' in s && typeof s.id === 'string' && s.id.length > 0) return s.id
  return null
}

function RequireUser(): ReactElement | null {
  const { shop, isLoaded } = useShop()
  const loc = useLocation()

  if (!isLoaded) return null
  if (!shop) return <Navigate to="/select-shop" state={{ from: loc }} replace />

  const stored = loadSelectedUserForShop(shop.id)
  const selectedUserId = pickSelectedUserId(stored)

  if (!selectedUserId) {
    const redirectTarget = `${loc.pathname}${loc.search}${loc.hash}`
    return <Navigate to="/select-shop" state={{ from: loc, redirectTo: redirectTarget }} replace />
  }

  return <Outlet />
}

export default function AppRoutes() {
  return (
    <Routes>
      {/* Étape 1 : choix de la boutique */}
      <Route path="/select-shop" element={<SelectShopPage />} />

      {/* Étape 2 : tout le reste requiert shop + user */}
      <Route element={<RequireShop />}>
        <Route element={<RequireUser />}>
          <Route path="/" element={<HomePage />} />

            <Route path="/inventory" element={<InventoryLayout />}>
              <Route index element={<Navigate to="location" replace />} />
              <Route path="location" element={<InventoryLocationStep />} />
              <Route path="count-type" element={<InventoryCountTypeStep />} />
              <Route path="session" element={<InventorySessionPage />} />
              <Route path="scan-camera" element={<ScanCameraPage />} />
            </Route>

          {/* Admin: à toi de décider si RequireUser est nécessaire ou non. Si oui, laisse ici. */}
          {/* <Route path="/admin" element={<AdminLayout />}>
               <Route index element={<AdminLocationsPage />} />
             </Route> */}
        </Route>
      </Route>

      {/* fallback */}
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
