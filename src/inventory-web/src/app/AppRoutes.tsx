import type { ReactElement } from 'react'
import { Routes, Route, Navigate, useLocation, Outlet } from 'react-router-dom'

// Ces deux-là sont des NAMED exports dans ton repo → import avec accolades
import { HomePage } from '@/app/pages/home/HomePage'
import { InventoryUserStep } from '@/app/pages/inventory/InventoryUserStep'

// Celui-ci est un default export dans ton repo → import sans accolades
import SelectShopPage from '@/app/pages/select-shop/SelectShopPage'

// Déjà présent dans ton projet (tu l’utilises ailleurs) ; garde-le tel quel
import { RequireShop } from '@/app/router/RequireShop'

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
    return <Navigate to="/select-user" state={{ from: loc, redirectTo: '/' }} replace />
  }

  return <Outlet />
}

export default function AppRoutes() {
  return (
    <Routes>
      {/* Étape 1 : choix de la boutique */}
      <Route path="/select-shop" element={<SelectShopPage />} />

      {/* Étape 2 : choix de l’utilisateur (réutilise InventoryUserStep que tu as déjà) */}
      <Route element={<RequireShop />}>
        <Route path="/select-user" element={<InventoryUserStep />} />
      </Route>

      {/* Étape 3 : tout le reste requiert shop + user */}
      <Route element={<RequireShop />}>
        <Route element={<RequireUser />}>
          <Route path="/" element={<HomePage />} />

          {/* Tes routes inventaire existantes, inchangées */}
          <Route path="/inventory">
            <Route index element={<Navigate to="count-type" replace />} />
            {/* si dans ton projet ces routes sont organisées via un layout, garde ton layout existant */}
            {/* Ex:
                <Route path="/inventory" element={<InventoryLayout />}>
                  <Route index element={<Navigate to="count-type" replace />} />
                  <Route path="location" element={<InventoryLocationStep />} />
                  <Route path="count-type" element={<InventoryCountTypeStep />} />
                  <Route path="session" element={<InventorySessionPage />} />
                </Route>
            */}
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
