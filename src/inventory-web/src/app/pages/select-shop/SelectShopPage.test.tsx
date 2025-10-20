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
  setShop: () => undefined,
  isLoaded: true,
  ...overrides,
})

const useShopMock = vi.hoisted(() =>
  vi.fn(() => ({
    shop: null,
    setShop: () => undefined,
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
  const cineShop: Shop = { id: '11111111-1111-1111-1111-111111111111', name: 'CinéBoutique Paris' }
  const lumiereShop: Shop = { id: '22222222-2222-2222-2222-222222222222', name: 'Lumière Lyon' }

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

  function renderPage(initialEntry: string | { pathname: string; state?: unknown } = '/select-shop') {
    return render(
      <ThemeProvider>
        <MemoryRouter initialEntries={[initialEntry]}>
          <SelectShopPage />
        </MemoryRouter>
      </ThemeProvider>,
    )
  }

  it("n'affiche pas de raccourci vers la page d’accueil", async () => {
    fetchShopsMock.mockResolvedValueOnce([])

    renderPage()

    await waitFor(() => {
      expect(screen.queryByTestId('btn-go-home')).not.toBeInTheDocument()
    })
  })

  it('navigue vers la page d’identification dès la sélection d’une boutique', async () => {
    fetchShopsMock.mockResolvedValueOnce([cineShop, lumiereShop])

    renderPage({ pathname: '/select-shop', state: { redirectTo: '/inventory' } })

    const shopRadio = await screen.findByRole('radio', { name: /Lumière/i })
    fireEvent.click(shopRadio)

    await waitFor(() => expect(setShopFn).toHaveBeenCalledWith(lumiereShop))
    await waitFor(() => expect(resetInventoryFn).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(clearSelectedUserMock).toHaveBeenCalledWith(lumiereShop.id))
    await waitFor(() =>
      expect(navigateMock).toHaveBeenCalledWith('/select-user', { state: { redirectTo: '/inventory' } }),
    )
  })

  it('réutilise la boutique active sans réinitialiser inutilement', async () => {
    fetchShopsMock.mockResolvedValueOnce([cineShop])
    useShopMock.mockReturnValue(
      createUseShopValue({
        shop: cineShop,
        setShop: setShopFn as unknown as UseShopValue['setShop'],
        isLoaded: true,
      }),
    )

    renderPage()

    const cardButton = await screen.findByRole('radio', { name: /CinéBoutique/i })
    fireEvent.click(cardButton)

    await waitFor(() => expect(setShopFn).toHaveBeenCalledWith(cineShop))
    expect(resetInventoryFn).not.toHaveBeenCalled()
    expect(clearSelectedUserMock).not.toHaveBeenCalled()
    await waitFor(() => expect(navigateMock).toHaveBeenCalledWith('/select-user', undefined))
  })

  it('bloque la navigation quand le GUID est invalide', async () => {
    const invalidShop: Shop = { id: 'invalid-id', name: 'Lumière test invalide' }
    fetchShopsMock.mockResolvedValueOnce([invalidShop])

    renderPage()

    const card = await screen.findByRole('radio', { name: /Lumière test invalide/i })
    fireEvent.click(card)

    const errorMessage = await screen.findByText(/Identifiant de boutique invalide/i)
    expect(errorMessage).toBeInTheDocument()

    expect(setShopFn).not.toHaveBeenCalled()
    expect(resetInventoryFn).not.toHaveBeenCalled()
    expect(clearSelectedUserMock).not.toHaveBeenCalled()
    expect(navigateMock).not.toHaveBeenCalled()
  })

  it('affiche un message d’erreur et permet de réessayer le chargement', async () => {
    const shops: Shop[] = [cineShop]
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
