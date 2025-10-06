import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, useLocation, type Location } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useEffect } from 'react'

import type { Shop } from '@/types/shop'
import { SelectShopPage } from '@/app/pages/select-shop/SelectShopPage'
import { ThemeProvider } from '@/theme/ThemeProvider'

const fetchShopsMock = vi.hoisted(() => vi.fn<(signal?: AbortSignal) => Promise<Shop[]>>())
const useShopMock = vi.hoisted(() =>
  vi.fn(() => ({ shop: null, setShop: () => undefined, isLoaded: true })),
)
const useInventoryMock = vi.hoisted(() => vi.fn(() => ({ reset: () => undefined })))
const loadShopMock = vi.hoisted(() => vi.fn())
const clearShopMock = vi.hoisted(() => vi.fn())
const clearSelectedUserMock = vi.hoisted(() => vi.fn())
const setShopFn = vi.hoisted(() => vi.fn())
const resetInventoryFn = vi.hoisted(() => vi.fn())

vi.mock('@/api/shops', () => ({
  fetchShops: (...args: Parameters<typeof fetchShopsMock>) => fetchShopsMock(...args),
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
  clearSelectedUserForShop: (...args: Parameters<typeof clearSelectedUserMock>) =>
    clearSelectedUserMock(...args),
  SELECTED_USER_STORAGE_PREFIX: 'cb.inventory.selectedUser',
}))

describe('SelectShopPage (routing)', () => {
  const shop: Shop = { id: '123e4567-e89b-12d3-a456-426614174000', name: 'Boutique 1' }

  beforeEach(() => {
    fetchShopsMock.mockReset()
    fetchShopsMock.mockResolvedValue([shop])
    setShopFn.mockReset()
    resetInventoryFn.mockReset()
    clearSelectedUserMock.mockReset()
    loadShopMock.mockReturnValue(null)
    clearShopMock.mockReset()

    useShopMock.mockReturnValue({ shop: null, setShop: setShopFn, isLoaded: true })
    useInventoryMock.mockReturnValue({ reset: resetInventoryFn })
  })

  const renderPage = (entry: string | { pathname: string; state?: unknown } = '/select-shop') =>
    render(
      <ThemeProvider>
        <MemoryRouter initialEntries={[entry]}>
          <SelectShopPage />
        </MemoryRouter>
      </ThemeProvider>,
    )

  it('redirige vers la page de sélection utilisateur après avoir choisi une boutique', async () => {
    const locations: Location[] = []
    const LocationObserver = () => {
      const location = useLocation()
      useEffect(() => {
        locations.push(location)
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

    await waitFor(() => expect(setShopFn).toHaveBeenCalledWith(shop))
    await waitFor(() => expect(resetInventoryFn).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(clearSelectedUserMock).toHaveBeenCalledWith(shop.id))
    await waitFor(() => expect(locations.at(-1)?.pathname).toBe('/select-user'))
  })

  it('conserve la cible de redirection transmise via location.state', async () => {
    const locations: Location[] = []
    const LocationObserver = () => {
      const location = useLocation()
      useEffect(() => {
        locations.push(location)
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

    await waitFor(() => expect(locations.at(-1)?.pathname).toBe('/select-user'))
    expect(locations.at(-1)?.state).toMatchObject({ redirectTo: '/inventory/location' })
  })
})
