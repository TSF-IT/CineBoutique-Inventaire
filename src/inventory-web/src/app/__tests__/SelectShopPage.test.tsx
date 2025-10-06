import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useEffect } from 'react'

import type { Shop } from '@/types/shop'
import type { ShopUser } from '@/types/user'
import { SelectShopPage } from '@/app/pages/select-shop/SelectShopPage'
import { ThemeProvider } from '@/theme/ThemeProvider'

const fetchShopsMock = vi.hoisted(() => vi.fn())
const fetchShopUsersMock = vi.hoisted(() => vi.fn())
const useShopMock = vi.hoisted(() =>
  vi.fn(() => ({ shop: null, setShop: () => undefined, isLoaded: true })),
)
const useInventoryMock = vi.hoisted(() =>
  vi.fn(() => ({
    selectedUser: null,
    setSelectedUser: () => undefined,
    reset: () => undefined,
    countType: null,
    setCountType: () => undefined,
    location: null,
    setLocation: () => undefined,
    sessionId: null,
    setSessionId: () => undefined,
    items: [],
    addOrIncrementItem: () => undefined,
    setQuantity: () => undefined,
    removeItem: () => undefined,
    clearSession: () => undefined,
  })),
)
const loadShopMock = vi.hoisted(() => vi.fn())
const clearShopMock = vi.hoisted(() => vi.fn())
const saveSelectedUserMock = vi.hoisted(() => vi.fn())
const loadSelectedUserMock = vi.hoisted(() => vi.fn())
const clearSelectedUserMock = vi.hoisted(() => vi.fn())
const setShopFn = vi.hoisted(() => vi.fn())
const setSelectedUserFn = vi.hoisted(() => vi.fn())
const resetInventoryFn = vi.hoisted(() => vi.fn())

vi.mock('@/api/shops', () => ({
  fetchShops: (...args: Parameters<typeof fetchShopsMock>) => fetchShopsMock(...args),
}))

vi.mock('@/app/api/shopUsers', () => ({
  fetchShopUsers: (...args: Parameters<typeof fetchShopUsersMock>) => fetchShopUsersMock(...args),
}))

vi.mock('@/state/ShopContext', () => ({
  useShop: () => useShopMock(),
}))

vi.mock('@/app/contexts/InventoryContext', () => ({
  useInventory: () => useInventoryMock(),
}))

vi.mock('@/lib/shopStorage', () => ({
  loadShop: () => loadShopMock(),
  clearShop: (...args: Parameters<typeof clearShopMock>) => clearShopMock(...args),
}))

vi.mock('@/lib/selectedUserStorage', () => ({
  saveSelectedUserForShop: (...args: Parameters<typeof saveSelectedUserMock>) => saveSelectedUserMock(...args),
  loadSelectedUserForShop: (...args: Parameters<typeof loadSelectedUserMock>) => loadSelectedUserMock(...args),
  clearSelectedUserForShop: (...args: Parameters<typeof clearSelectedUserMock>) => clearSelectedUserMock(...args),
  SELECTED_USER_STORAGE_PREFIX: 'cb.inventory.selectedUser',
}))

describe('SelectShopPage', () => {
  const shop: Shop = { id: '123e4567-e89b-12d3-a456-426614174000', name: 'Boutique 1' }
  const user: ShopUser = {
    id: 'user-1',
    displayName: 'Utilisateur 1',
    login: 'user1',
    shopId: shop.id,
    isAdmin: false,
    disabled: false,
  }

  beforeEach(() => {
    fetchShopUsersMock.mockReset()
    fetchShopsMock.mockResolvedValue([shop])
    fetchShopUsersMock.mockImplementation(async () => [user])
    setShopFn.mockReset()
    setSelectedUserFn.mockReset()
    resetInventoryFn.mockReset()
    useShopMock.mockReturnValue({ shop: null, setShop: setShopFn, isLoaded: true })
    useInventoryMock.mockReturnValue({
      selectedUser: null,
      setSelectedUser: setSelectedUserFn,
      reset: resetInventoryFn,
      countType: null,
      setCountType: vi.fn(),
      location: null,
      setLocation: vi.fn(),
      sessionId: null,
      setSessionId: vi.fn(),
      items: [],
      addOrIncrementItem: vi.fn(),
      setQuantity: vi.fn(),
      removeItem: vi.fn(),
      clearSession: vi.fn(),
    })
    loadShopMock.mockReturnValue(null)
    clearShopMock.mockReset()
    saveSelectedUserMock.mockReset()
    loadSelectedUserMock.mockReturnValue(null)
    clearSelectedUserMock.mockReset()
  })

  it('affiche les utilisateurs après la sélection de la boutique et redirige après sélection utilisateur', async () => {
    const locations: string[] = []
    const LocationObserver = () => {
      const location = useLocation()
      useEffect(() => {
        locations.push(location.pathname)
      }, [location])
      return null
    }

    render(
      <ThemeProvider>
        <MemoryRouter initialEntries={['/select-shop']}>
          <LocationObserver />
          <SelectShopPage />
        </MemoryRouter>
      </ThemeProvider>,
    )

    const shopButton = await screen.findByRole('radio', { name: /boutique 1/i })
    fireEvent.click(shopButton)

    await waitFor(() => expect(fetchShopUsersMock).toHaveBeenCalledWith(shop.id))

    const userButton = await screen.findByRole('radio', { name: /utilisateur 1/i })
    fireEvent.click(userButton)

    await waitFor(() => expect(saveSelectedUserMock).toHaveBeenCalledWith(shop.id, user))
    await waitFor(() => expect(setSelectedUserFn).toHaveBeenCalledWith(user))
    await waitFor(() => expect(locations.at(-1)).toBe('/'))
  })

  it('respecte la cible de redirection transmise via location.state', async () => {
    const locations: string[] = []
    const LocationObserver = () => {
      const location = useLocation()
      useEffect(() => {
        locations.push(location.pathname)
      }, [location])
      return null
    }

    render(
      <ThemeProvider>
        <MemoryRouter
          initialEntries={[{ pathname: '/select-shop', state: { redirectTo: '/inventory/location' } }]}
        >
          <LocationObserver />
          <SelectShopPage />
        </MemoryRouter>
      </ThemeProvider>,
    )

    const shopButton = await screen.findByRole('radio', { name: /boutique 1/i })
    fireEvent.click(shopButton)

    const userButton = await screen.findByRole('radio', { name: /utilisateur 1/i })
    fireEvent.click(userButton)

    await waitFor(() => expect(locations.at(-1)).toBe('/inventory/location'))
  })
})
