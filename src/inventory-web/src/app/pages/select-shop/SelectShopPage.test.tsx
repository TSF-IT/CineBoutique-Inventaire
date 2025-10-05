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

  it('navigue vers l’accueil après sélection depuis la liste', async () => {
    const shops: Shop[] = [
      { id: '11111111-1111-1111-1111-111111111111', name: 'Boutique 1' },
      { id: '22222222-2222-2222-2222-222222222222', name: 'Boutique 2' },
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

    expect(screen.queryByRole('button', { name: 'Continuer' })).not.toBeInTheDocument()

    fireEvent.change(select, { target: { value: shops[1].id } })

    await waitFor(() => {
      expect(setShopSpy).toHaveBeenCalledWith(shops[1])
      expect(navigateMock).toHaveBeenCalledWith('/select-user', {
        replace: true,
        state: { redirectTo: '/' },
      })
    })
  })

  it('pré-sélectionne la boutique existante quand elle est disponible', async () => {
    const shops: Shop[] = [
      { id: '33333333-3333-3333-3333-333333333333', name: 'Boutique 1' },
      { id: '44444444-4444-4444-4444-444444444444', name: 'Boutique 2' },
    ]
    fetchShopsMock.mockResolvedValueOnce(shops)
    const setShopSpy = vi.fn()
    useShopMock.mockReturnValue({ shop: shops[0], setShop: setShopSpy, isLoaded: true })

    render(
      <ThemeProvider>
        <SelectShopPage />
      </ThemeProvider>,
    )

    const selectedRadio = await screen.findByRole('radio', { name: /Boutique 1/i })
    expect(selectedRadio).toHaveAttribute('aria-checked', 'true')

    fireEvent.click(selectedRadio)

    await waitFor(() => {
      expect(setShopSpy).toHaveBeenCalledWith(shops[0])
      expect(navigateMock).toHaveBeenCalledWith('/select-user', {
        replace: true,
        state: { redirectTo: '/' },
      })
    })
  })

  it('bloque la navigation quand le GUID est invalide', async () => {
    const shops: Shop[] = [{ id: 'invalid-id', name: 'Boutique invalide' }]
    fetchShopsMock.mockResolvedValueOnce(shops)
    const setShopSpy = vi.fn()
    useShopMock.mockReturnValue({ shop: null, setShop: setShopSpy, isLoaded: true })

    render(
      <ThemeProvider>
        <SelectShopPage />
      </ThemeProvider>,
    )

    const card = await screen.findByRole('radio', { name: /Boutique invalide/i })
    fireEvent.click(card)

    expect(await screen.findByText(/Identifiant de boutique invalide/i)).toBeInTheDocument()
    expect(setShopSpy).not.toHaveBeenCalled()
    expect(navigateMock).not.toHaveBeenCalled()
  })

  it('affiche un message d’erreur et permet de réessayer le chargement', async () => {
    const shops: Shop[] = [{ id: '55555555-5555-5555-5555-555555555555', name: 'Boutique 1' }]
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

    const selects = await screen.findAllByLabelText('Boutique')
    expect(selects.length).toBeGreaterThan(0)

    consoleErrorSpy.mockRestore()
  })
})
