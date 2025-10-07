import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Shop } from '@/types/shop'
import { SelectShopPage } from './SelectShopPage'
import { ThemeProvider } from '@/theme/ThemeProvider'

const fetchShopsMock = vi.hoisted(() => vi.fn<(signal?: AbortSignal) => Promise<Shop[]>>())

type UseShopValue = {
  shop: Shop | null
  setShop: (shop: Shop | null) => void
  isLoaded: boolean
}

const createUseShopValue = (overrides: Partial<UseShopValue> = {}): UseShopValue => ({
  shop: null,
  setShop: (_shop: Shop | null) => undefined,
  isLoaded: true,
  ...overrides,
})

const useShopMock = vi.hoisted(() =>
  vi.fn(() => ({
    shop: null,
    setShop: (_shop: Shop | null) => undefined,
    isLoaded: true,
  } as UseShopValue)),
)

const useInventoryMock = vi.hoisted(() =>
  vi.fn(() => ({
    reset: () => undefined,
  })),
)

const navigateMock = vi.hoisted(() => vi.fn())
const setShopFn = vi.hoisted(() => vi.fn())
const resetInventoryFn = vi.hoisted(() => vi.fn())
const clearSelectedUserMock = vi.hoisted(() => vi.fn())

vi.mock('@/api/shops', () => ({
  fetchShops: (signal?: AbortSignal) => fetchShopsMock(signal),
}))

vi.mock('@/state/ShopContext', () => ({
  useShop: () => useShopMock(),
}))

vi.mock('@/app/contexts/InventoryContext', () => ({
  useInventory: () => useInventoryMock(),
}))

vi.mock('@/lib/selectedUserStorage', () => ({
  clearSelectedUserForShop: (...args: Parameters<typeof clearSelectedUserMock>) =>
    clearSelectedUserMock(...args),
  SELECTED_USER_STORAGE_PREFIX: 'cb.inventory.selectedUser',
}))

vi.mock('react-router-dom', async (importOriginal) => {
  const actual = await importOriginal<typeof import('react-router-dom')>()
  return {
    ...actual,
    useNavigate: () => navigateMock,
  }
})

describe('SelectShopPage', () => {
  const shopA: Shop = { id: '11111111-1111-1111-1111-111111111111', name: 'Boutique 1' }
  const shopB: Shop = { id: '22222222-2222-2222-2222-222222222222', name: 'Boutique 2' }

  beforeEach(() => {
    fetchShopsMock.mockReset()
    setShopFn.mockReset()
    resetInventoryFn.mockReset()
    clearSelectedUserMock.mockReset()
    navigateMock.mockReset()

    useShopMock.mockReturnValue(
      createUseShopValue({
        shop: null,
        setShop: setShopFn as unknown as UseShopValue['setShop'],
        isLoaded: true,
      }),
    )

    useInventoryMock.mockReturnValue({
      reset: resetInventoryFn,
    })
  })

  const renderPage = (initialEntry: string | { pathname: string; state?: unknown } = '/select-shop') =>
    render(
      <ThemeProvider>
        <MemoryRouter initialEntries={[initialEntry]}>
          <SelectShopPage />
        </MemoryRouter>
      </ThemeProvider>,
    )

  it('affiche un raccourci vers la page d’accueil', async () => {
    fetchShopsMock.mockResolvedValueOnce([])

    renderPage()

    const homeLink = await screen.findByTestId('btn-go-home')
    expect(homeLink).toBeInTheDocument()
    expect(homeLink).toHaveAttribute('href', '/')
  })

  it('navigue vers la page d’identification dès la sélection d’une boutique', async () => {
    fetchShopsMock.mockResolvedValueOnce([shopA, shopB])

    renderPage({ pathname: '/select-shop', state: { redirectTo: '/inventory' } })

    const shopRadio = await screen.findByRole('radio', { name: /Boutique 2/i })
    fireEvent.click(shopRadio)

    await waitFor(() => expect(setShopFn).toHaveBeenCalledWith(shopB))
    await waitFor(() => expect(resetInventoryFn).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(clearSelectedUserMock).toHaveBeenCalledWith(shopB.id))
    await waitFor(() =>
      expect(navigateMock).toHaveBeenCalledWith('/select-user', { state: { redirectTo: '/inventory' } }),
    )
  })

  it('réutilise la boutique active sans réinitialiser inutilement', async () => {
    fetchShopsMock.mockResolvedValueOnce([shopA])
    useShopMock.mockReturnValue(
      createUseShopValue({
        shop: shopA,
        setShop: setShopFn as unknown as UseShopValue['setShop'],
        isLoaded: true,
      }),
    )

    renderPage()

    const cardButton = await screen.findByRole('radio', { name: /Boutique 1/i })
    fireEvent.click(cardButton)

    await waitFor(() => expect(setShopFn).toHaveBeenCalledWith(shopA))
    expect(resetInventoryFn).not.toHaveBeenCalled()
    expect(clearSelectedUserMock).not.toHaveBeenCalled()
    await waitFor(() => expect(navigateMock).toHaveBeenCalledWith('/select-user', undefined))
  })

  it('bloque la navigation quand le GUID est invalide', async () => {
    const invalidShop: Shop = { id: 'invalid-id', name: 'Boutique invalide' }
    fetchShopsMock.mockResolvedValueOnce([invalidShop])

    renderPage()

    const card = await screen.findByRole('radio', { name: /Boutique invalide/i })
    fireEvent.click(card)

    const errorMessage = await screen.findByText(/Identifiant de boutique invalide/i)
    expect(errorMessage).toBeInTheDocument()

    expect(setShopFn).not.toHaveBeenCalled()
    expect(resetInventoryFn).not.toHaveBeenCalled()
    expect(clearSelectedUserMock).not.toHaveBeenCalled()
    expect(navigateMock).not.toHaveBeenCalled()
  })

  it('affiche un message d’erreur et permet de réessayer le chargement', async () => {
    const shops: Shop[] = [shopA]
    fetchShopsMock.mockRejectedValueOnce(new Error('API indisponible'))
    fetchShopsMock.mockRejectedValueOnce(new Error('API indisponible'))
    fetchShopsMock.mockResolvedValueOnce(shops)
    const consoleErrorSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})

    renderPage()

    expect(await screen.findByText(/API indisponible/i)).toBeInTheDocument()

    const retryButton = await screen.findByRole('button', { name: /Réessayer/i })
    fireEvent.click(retryButton)

    await waitFor(() => expect(fetchShopsMock).toHaveBeenCalledTimes(2))

    consoleErrorSpy.mockRestore()
  })
})
