import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'

import { SelectShopPage } from '../../src/app/pages/select-shop/SelectShopPage'
import { ThemeProvider } from '../../src/theme/ThemeProvider'

type Shop = {
  id: string
  name: string
}

const fetchShopsMock = vi.fn<(signal?: AbortSignal) => Promise<Shop[]>>()
const navigateMock = vi.fn()
const setShopMock = vi.fn()
const resetInventoryMock = vi.fn()
const clearSelectedUserForShopMock = vi.fn<(shopId: string) => void>()

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom')

  return {
    ...actual,
    useNavigate: () => navigateMock,
    useLocation: () => ({ state: null }),
  }
})

vi.mock('../../src/api/shops', () => ({
  fetchShops: (signal?: AbortSignal) => fetchShopsMock(signal),
}))

const shopContextValue = { shop: null as Shop | null, setShop: setShopMock }

vi.mock('../../src/state/ShopContext', () => ({
  useShop: () => shopContextValue,
}))

vi.mock('../../src/app/contexts/InventoryContext', () => ({
  useInventory: () => ({ reset: resetInventoryMock }),
}))

vi.mock('../../src/lib/selectedUserStorage', () => ({
  clearSelectedUserForShop: (shopId: string) => clearSelectedUserForShopMock(shopId),
}))

describe('SelectShopPage', () => {
  beforeEach(() => {
    fetchShopsMock.mockReset()
    navigateMock.mockReset()
    setShopMock.mockReset()
    resetInventoryMock.mockReset()
    clearSelectedUserForShopMock.mockReset()
    shopContextValue.shop = null
  })

  it('redirige immédiatement après la sélection d’une boutique en mode carte', async () => {
    const shops: Shop[] = [
      { id: '11111111-1111-4111-8111-111111111111', name: 'Boutique Alpha' },
      { id: '22222222-2222-4222-8222-222222222222', name: 'Boutique Beta' },
    ]
    fetchShopsMock.mockResolvedValueOnce(shops)

    const user = userEvent.setup()
    render(
      <ThemeProvider>
        <MemoryRouter initialEntries={[{ pathname: '/select-shop' }]}>
          <SelectShopPage />
        </MemoryRouter>
      </ThemeProvider>,
    )

    const choice = await screen.findByRole('radio', { name: /Boutique Alpha/i })
    await user.click(choice)

    await waitFor(() => {
      expect(navigateMock).toHaveBeenCalledWith('/select-user', undefined)
    })

    expect(setShopMock).toHaveBeenCalledWith(shops[0])
    expect(resetInventoryMock).toHaveBeenCalledTimes(1)
    expect(clearSelectedUserForShopMock).toHaveBeenCalledWith(shops[0].id)
    expect(screen.queryByRole('button', { name: /Continuer/i })).not.toBeInTheDocument()
  })

  it('redirige lors du choix d’une boutique via la liste déroulante', async () => {
    const shops: Shop[] = Array.from({ length: 6 }, (_, index) => ({
      id: `${index + 1}${index + 1}${index + 1}${index + 1}${index + 1}${index + 1}${index + 1}${index + 1}-${index + 1}${index + 1}${index + 1}${index + 1}-${index + 1}${index + 1}${index + 1}${index + 1}-${index + 1}${index + 1}${index + 1}${index + 1}-${index + 1}${index + 1}${index + 1}${index + 1}${index + 1}${index + 1}${index + 1}${index + 1}${index + 1}${index + 1}${index + 1}${index + 1}`,
      name: `Boutique ${index + 1}`,
    }))
    fetchShopsMock.mockResolvedValueOnce(shops)

    const user = userEvent.setup()
    render(
      <ThemeProvider>
        <MemoryRouter initialEntries={[{ pathname: '/select-shop' }]}>
          <SelectShopPage />
        </MemoryRouter>
      </ThemeProvider>,
    )

    const select = await screen.findByRole('combobox', { name: /Boutique/i })
    await user.selectOptions(select, shops[2].id)

    await waitFor(() => {
      expect(navigateMock).toHaveBeenCalledWith('/select-user', undefined)
    })

    expect(setShopMock).toHaveBeenCalledWith(shops[2])
    expect(resetInventoryMock).toHaveBeenCalledTimes(1)
    expect(clearSelectedUserForShopMock).toHaveBeenCalledWith(shops[2].id)
  })
})
