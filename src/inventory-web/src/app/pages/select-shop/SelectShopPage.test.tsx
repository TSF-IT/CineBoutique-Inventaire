import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Shop } from '@/types/shop'
import { SelectShopPage } from './SelectShopPage'
import { ThemeProvider } from '@/theme/ThemeProvider'

const fetchShopsMock = vi.hoisted(() =>
  vi.fn<(signal?: AbortSignal) => Promise<Shop[]>>()
)
const useShopMock = vi.hoisted(() => vi.fn())
const navigateMock = vi.hoisted(() => vi.fn())

vi.mock('@/api/shops', () => ({
  fetchShops: (signal?: AbortSignal) => fetchShopsMock(signal),
}))

vi.mock('@/state/ShopContext', () => ({
  useShop: () => useShopMock(),
}))

vi.mock('react-router-dom', async (importOriginal) => {
  const actual = await importOriginal<typeof import('react-router-dom')>()
  return {
    ...actual,
    useNavigate: () => navigateMock,
  }
})

describe('SelectShopPage', () => {
  beforeEach(() => {
    fetchShopsMock.mockReset()
    useShopMock.mockReset()
    navigateMock.mockReset()
  })

  it('affiche les boutiques et permet de continuer après sélection', async () => {
    const shops: Shop[] = [
      { id: 'shop-1', name: 'Boutique 1' },
      { id: 'shop-2', name: 'Boutique 2' },
    ]
    fetchShopsMock.mockResolvedValueOnce(shops)
    const setShopSpy = vi.fn()
    useShopMock.mockReturnValue({ shop: null, setShop: setShopSpy, isLoaded: true })

    render(
      <ThemeProvider>
        <SelectShopPage />
      </ThemeProvider>,
    )

    const select = await screen.findByLabelText('Boutique')
    expect(select).toHaveValue('')

    const continueButton = screen.getByRole('button', { name: 'Continuer' })
    expect(continueButton).toBeDisabled()

    fireEvent.change(select, { target: { value: 'shop-2' } })
    expect(continueButton).not.toBeDisabled()

    fireEvent.click(continueButton)

    expect(setShopSpy).toHaveBeenCalledWith(shops[1])
    expect(navigateMock).toHaveBeenCalledWith('/', { replace: true })
  })

  it('pré-sélectionne la boutique existante quand elle est disponible', async () => {
    const shops: Shop[] = [
      { id: 'shop-1', name: 'Boutique 1' },
      { id: 'shop-2', name: 'Boutique 2' },
    ]
    fetchShopsMock.mockResolvedValueOnce(shops)
    const setShopSpy = vi.fn()
    useShopMock.mockReturnValue({ shop: shops[0], setShop: setShopSpy, isLoaded: true })

    render(
      <ThemeProvider>
        <SelectShopPage />
      </ThemeProvider>,
    )

    await screen.findByLabelText('Boutique')

    const continueButtons = screen.getAllByRole('button', { name: 'Continuer' })
    continueButtons.forEach((button) => {
      expect(button).not.toBeDisabled()
      fireEvent.click(button)
    })

    expect(setShopSpy).toHaveBeenCalledWith(shops[0])
  })

  it('affiche un message d’erreur et permet de réessayer le chargement', async () => {
    const shops: Shop[] = [{ id: 'shop-1', name: 'Boutique 1' }]
    fetchShopsMock
      .mockRejectedValueOnce(new Error('API indisponible'))
      .mockResolvedValueOnce(shops)
    const setShopSpy = vi.fn()
    useShopMock.mockReturnValue({ shop: null, setShop: setShopSpy, isLoaded: true })

    const consoleErrorSpy = vi.spyOn(console, 'error').mockImplementation(() => {})

    render(
      <ThemeProvider>
        <SelectShopPage />
      </ThemeProvider>,
    )

    expect(await screen.findByText(/API indisponible/)).toBeInTheDocument()

    const retryButton = screen.getByRole('button', { name: 'Réessayer' })
    fireEvent.click(retryButton)

    await waitFor(() => {
      expect(fetchShopsMock).toHaveBeenCalledTimes(2)
    })

    await screen.findByLabelText('Boutique')

    consoleErrorSpy.mockRestore()
  })
})
