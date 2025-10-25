import { render, screen } from '@testing-library/react'
import type { ReactNode } from 'react'
import { useLayoutEffect } from 'react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, describe, expect, it } from 'vitest'

import { InventoryProvider, useInventory } from '../../../contexts/InventoryContext'
import type { Location } from '../../../types/inventory'
import { CountType } from '../../../types/inventory'
import { InventoryLayout } from '../InventoryLayout'

import { SHOP_STORAGE_KEY } from '@/lib/shopStorage'
import { ShopProvider, useShop } from '@/state/ShopContext'
import { ThemeProvider } from '@/theme/ThemeProvider'
import type { Shop } from '@/types/shop'
import type { ShopUser } from '@/types/user'


const shop: Shop = { id: 'shop-test', name: 'Boutique test', kind: 'boutique' }

const user: ShopUser = {
  id: 'user-test',
  shopId: shop.id,
  login: 'user.test',
  displayName: 'Utilisateur Test',
  isAdmin: false,
  disabled: false,
}

const baseLocation: Location = {
  id: '00000000-0000-0000-0000-000000000001',
  code: 'Z1',
  label: 'Zone test',
  isBusy: false,
  busyBy: null,
  activeRunId: null,
  activeCountType: null,
  activeStartedAtUtc: null,
  countStatuses: [],
  disabled: false,
}

const InventoryStateInitializer = ({
  children,
  location = null,
  countType = null,
}: {
  children: ReactNode
  location?: Location | null
  countType?: CountType | null
}) => {
  const { setSelectedUser, setLocation, setCountType } = useInventory()
  useLayoutEffect(() => {
    setSelectedUser(user)
    if (location) {
      setLocation(location)
    }
    setCountType(countType)
  }, [countType, location, setCountType, setLocation, setSelectedUser])

  return <>{children}</>
}

const ShopInitializer = ({ shop }: { shop: Shop }) => {
  const { setShop } = useShop()

  useLayoutEffect(() => {
    setShop(shop)
  }, [setShop, shop])

  return null
}

afterEach(() => {
  window.localStorage.clear()
})

describe('InventoryLayout', () => {
  it.each<{
    path: string
    expectedHref: string
    initializerProps?: { location?: Location | null; countType?: CountType | null }
  }>([
    { path: '/inventory/location', expectedHref: '/' },
    { path: '/inventory/count-type', expectedHref: '/inventory/location', initializerProps: { location: baseLocation } },
    {
      path: '/inventory/session',
      expectedHref: '/inventory/count-type',
      initializerProps: { location: baseLocation, countType: CountType.Count1 },
    },
    {
      path: '/inventory/scan-camera',
      expectedHref: '/inventory/session',
      initializerProps: { location: baseLocation, countType: CountType.Count1 },
    },
  ])('pointe le lien de retour attendu pour %s', ({ path, expectedHref, initializerProps }) => {
    window.localStorage.setItem(SHOP_STORAGE_KEY, JSON.stringify(shop))

    render(
      <ThemeProvider>
        <MemoryRouter initialEntries={[path]}>
          <ShopProvider>
            <ShopInitializer shop={shop} />
            <InventoryProvider>
              <InventoryStateInitializer {...initializerProps}>
                <Routes>
                  <Route path="/inventory" element={<InventoryLayout />}>
                    <Route path="location" element={<div data-testid="inventory-location-step" />} />
                    <Route path="count-type" element={<div data-testid="inventory-count-type-step" />} />
                    <Route path="session" element={<div data-testid="inventory-session-step" />} />
                    <Route path="scan-camera" element={<div data-testid="inventory-scan-step" />} />
                  </Route>
                </Routes>
              </InventoryStateInitializer>
            </InventoryProvider>
          </ShopProvider>
        </MemoryRouter>
      </ThemeProvider>,
    )

    const homeLink = screen.getByTestId('btn-go-home')
    expect(homeLink).toHaveAttribute('href', expectedHref)
  })
})
