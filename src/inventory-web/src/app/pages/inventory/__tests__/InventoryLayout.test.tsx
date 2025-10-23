import { render, screen, within } from '@testing-library/react'
import type { ReactNode } from 'react'
import { useLayoutEffect } from 'react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, describe, expect, it } from 'vitest'
import { InventoryLayout } from '../InventoryLayout'
import { InventoryProvider, useInventory } from '../../../contexts/InventoryContext'
import { ShopProvider, useShop } from '@/state/ShopContext'
import type { Location } from '../../../types/inventory'
import { ThemeProvider } from '@/theme/ThemeProvider'
import type { Shop } from '@/types/shop'
import type { ShopUser } from '@/types/user'
import { SHOP_STORAGE_KEY } from '@/lib/shopStorage'

const shop: Shop = { id: 'shop-test', name: 'Boutique test', kind: 'boutique' }

const user: ShopUser = {
  id: 'user-test',
  shopId: shop.id,
  login: 'user.test',
  displayName: 'Utilisateur Test',
  isAdmin: false,
  disabled: false,
}

const location: Location = {
  id: 'location-test',
  code: 'Z01',
  label: 'Zone test',
  isBusy: false,
  busyBy: null,
  activeRunId: null,
  activeCountType: null,
  activeStartedAtUtc: null,
  countStatuses: [],
}

const InventoryStateInitializer = ({ children }: { children: ReactNode }) => {
  const { setSelectedUser, setLocation, setCountType } = useInventory()
  useLayoutEffect(() => {
    setSelectedUser(user)
    setLocation(location)
    setCountType(2)
  }, [setCountType, setLocation, setSelectedUser])

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
  it('pointe le lien de retour vers la sélection utilisateur', () => {
    window.localStorage.setItem(SHOP_STORAGE_KEY, JSON.stringify(shop))

    render(
      <ThemeProvider>
        <MemoryRouter initialEntries={['/inventory/location']}>
          <ShopProvider>
            <ShopInitializer shop={shop} />
            <InventoryProvider>
              <InventoryStateInitializer>
                <Routes>
                  <Route path="/inventory" element={<InventoryLayout />}>
                    <Route path="location" element={<div data-testid="inventory-location-step" />} />
                  </Route>
                </Routes>
              </InventoryStateInitializer>
            </InventoryProvider>
          </ShopProvider>
        </MemoryRouter>
      </ThemeProvider>,
    )

    const homeLink = screen.getByTestId('btn-go-home')
    expect(homeLink).toHaveAttribute('href', '/select-user')
  })

  it('affiche le récapitulatif utilisateur, zone et comptage', () => {
    window.localStorage.setItem(SHOP_STORAGE_KEY, JSON.stringify(shop))

    render(
      <ThemeProvider>
        <MemoryRouter initialEntries={['/inventory/location']}>
          <ShopProvider>
            <ShopInitializer shop={shop} />
            <InventoryProvider>
              <InventoryStateInitializer>
                <Routes>
                  <Route path="/inventory" element={<InventoryLayout />}>
                    <Route path="location" element={<div data-testid="inventory-location-step" />} />
                  </Route>
                </Routes>
              </InventoryStateInitializer>
            </InventoryProvider>
          </ShopProvider>
        </MemoryRouter>
      </ThemeProvider>,
    )

    const summary = screen.getByTestId('inventory-summary-info')
    expect(summary).toBeInTheDocument()
    expect(within(summary).getByText(/Utilisateur/i)).toBeInTheDocument()
    expect(within(summary).getByText('Utilisateur Test')).toBeInTheDocument()
    expect(within(summary).getByText(/Zone/i)).toBeInTheDocument()
    expect(within(summary).getByText('Zone test')).toBeInTheDocument()
    expect(within(summary).getByText(/Comptage/i)).toBeInTheDocument()
    expect(within(summary).getByText('2')).toBeInTheDocument()
  })
})
