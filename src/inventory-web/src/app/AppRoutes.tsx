import { Routes, Route, Navigate } from 'react-router-dom'

import { HomePage } from '@/app/pages/home/HomePage'
import { InventoryCountTypeStep } from '@/app/pages/inventory/InventoryCountTypeStep'
import { InventoryLayout } from '@/app/pages/inventory/InventoryLayout'
import { InventoryLocationStep } from '@/app/pages/inventory/InventoryLocationStep'
import { InventorySessionPage } from '@/app/pages/inventory/InventorySessionPage'
import { ScanCameraPage } from '@/app/pages/inventory/ScanCameraPage'
import { SelectShopPage } from '@/app/pages/select-shop/SelectShopPage'
import RequireInventorySession from '@/app/router/RequireInventorySession'
import RequireShop from '@/app/router/RequireShop'
import RequireUser from '@/app/router/RequireUser'

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
            <Route element={<RequireInventorySession />}>
              <Route path="session" element={<InventorySessionPage />} />
              <Route path="scan-camera" element={<ScanCameraPage />} />
            </Route>
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
