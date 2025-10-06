import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Shop } from '@/types/shop'
import type { ShopUser } from '@/types/user'
import { SelectShopPage } from './SelectShopPage'
import { ThemeProvider } from '@/theme/ThemeProvider'

const fetchShopsMock = vi.hoisted(() => vi.fn<(signal?: AbortSignal) => Promise<Shop[]>>())
const fetchShopUsersMock = vi.hoisted(() => vi.fn<(shopId: string) => Promise<ShopUser[]>>())
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
const navigateMock = vi.hoisted(() => vi.fn())
const setShopFn = vi.hoisted(() => vi.fn())
const setSelectedUserFn = vi.hoisted(() => vi.fn())
const resetInventoryFn = vi.hoisted(() => vi.fn())
const saveSelectedUserMock = vi.hoisted(() => vi.fn())
const loadSelectedUserMock = vi.hoisted(() => vi.fn())
const clearSelectedUserMock = vi.hoisted(() => vi.fn())

vi.mock('@/api/shops', () => ({
  fetchShops: (signal?: AbortSignal) => fetchShopsMock(signal),
}))

vi.mock('@/app/api/shopUsers', () => ({
  fetchShopUsers: (shopId: string) => fetchShopUsersMock(shopId),
}))

vi.mock('@/state/ShopContext', () => ({
  useShop: () => useShopMock(),
}))

vi.mock('@/app/contexts/InventoryContext', () => ({
  useInventory: () => useInventoryMock(),
}))

vi.mock('@/lib/selectedUserStorage', () => ({
  saveSelectedUserForShop: (...args: Parameters<typeof saveSelectedUserMock>) =>
    saveSelectedUserMock(...args),
  loadSelectedUserForShop: (...args: Parameters<typeof loadSelectedUserMock>) =>
    loadSelectedUserMock(...args),
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

describe('SelectShopPage (integration)', () => {
  const shopA: Shop = { id: '11111111-1111-1111-1111-111111111111', name: 'Boutique 1' }
  const shopB: Shop = { id: '22222222-2222-2222-2222-222222222222', name: 'Boutique 2' }
  const defaultUser: ShopUser = {
    id: 'user-1',
    displayName: 'Utilisateur 1',
    login: 'user1',
    shopId: shopB.id,
    isAdmin: false,
    disabled: false,
  }

  beforeEach(() => {
    fetchShopsMock.mockReset()
    fetchShopUsersMock.mockReset()
    setShopFn.mockReset()
    setSelectedUserFn.mockReset()
    resetInventoryFn.mockReset()
    saveSelectedUserMock.mockReset()
    loadSelectedUserMock.mockReset()
    clearSelectedUserMock.mockReset()
    navigateMock.mockReset()

    fetchShopUsersMock.mockImplementation(async () => [defaultUser])
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
  })

  const renderPage = () =>
    render(
      <ThemeProvider>
        <MemoryRouter initialEntries={['/select-shop']}>
          <SelectShopPage />
        </MemoryRouter>
      </ThemeProvider>,
    )

  it("navigue vers l’accueil après sélection complète", async () => {
    fetchShopsMock.mockResolvedValueOnce([shopA, shopB])

    renderPage()

    const shopRadio = await screen.findByRole('radio', { name: /Boutique 2/i })
    fireEvent.click(shopRadio)

    await waitFor(() => expect(fetchShopUsersMock).toHaveBeenCalledWith(shopB.id))

    const userRadio = await screen.findByRole('radio', { name: /Utilisateur 1/i })
    fireEvent.click(userRadio)

    await waitFor(() => expect(setShopFn).toHaveBeenCalledWith(shopB))
    await waitFor(() => expect(setSelectedUserFn).toHaveBeenCalledWith(defaultUser))
    await waitFor(() => expect(saveSelectedUserMock).toHaveBeenCalledWith(shopB.id, defaultUser))
    await waitFor(() => expect(navigateMock).toHaveBeenCalledWith('/', { replace: true }))
  })

  it('affiche un bouton continuer pour la boutique déjà mémorisée', async () => {
    fetchShopsMock.mockResolvedValueOnce([shopA, shopB])
    loadSelectedUserMock.mockReturnValue({ userId: defaultUser.id })
    useShopMock.mockReturnValue({ shop: shopA, setShop: setShopFn, isLoaded: true })
    fetchShopUsersMock.mockImplementationOnce(async () => [{ ...defaultUser, shopId: shopA.id }])

    renderPage()

    const storedUser = { ...defaultUser, shopId: shopA.id }

    await waitFor(() => expect(setSelectedUserFn).toHaveBeenCalledWith(storedUser))

    const continueButton = await screen.findByRole('button', { name: /Continuer/i })
    fireEvent.click(continueButton)

    await waitFor(() => expect(saveSelectedUserMock).toHaveBeenCalledWith(shopA.id, storedUser))
    await waitFor(() => expect(navigateMock).toHaveBeenCalledWith('/', { replace: true }))
  })

  it('bloque la navigation quand le GUID est invalide', async () => {
    const invalidShop: Shop = { id: 'invalid-id', name: 'Boutique invalide' }
    fetchShopsMock.mockResolvedValueOnce([invalidShop])

    renderPage()

    const card = await screen.findByRole('radio', { name: /Boutique invalide/i })
    fireEvent.click(card)

    expect(await screen.findByText(/Identifiant de boutique invalide/i)).toBeInTheDocument()
    expect(setShopFn).not.toHaveBeenCalled()
    expect(fetchShopUsersMock).not.toHaveBeenCalled()
    expect(navigateMock).not.toHaveBeenCalled()
  })

  it('affiche un message d’erreur et permet de réessayer le chargement', async () => {
    const shops: Shop[] = [shopA]
    fetchShopsMock.mockRejectedValueOnce(new Error('API indisponible'))
    fetchShopsMock.mockResolvedValueOnce(shops)
    const consoleErrorSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})

    renderPage()

    expect(
      await screen.findByText(/Impossible de charger la liste des boutiques/i),
    ).toBeInTheDocument()

    const retryButton = screen.getByRole('button', { name: 'Réessayer' })
    fireEvent.click(retryButton)

    await waitFor(() => expect(fetchShopsMock).toHaveBeenCalledTimes(2))

    consoleErrorSpy.mockRestore()
  })
})
