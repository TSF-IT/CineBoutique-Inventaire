import { render, screen } from '@testing-library/react'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useEffect } from 'react'
import type { Shop } from '@/types/shop'
import { SELECTED_USER_STORAGE_PREFIX } from '@/lib/selectedUserStorage'

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

vi.mock('@/state/ShopContext', () => ({
  useShop: () => useShopMock(),
}))

vi.mock('@/app/pages/home/HomePage', () => ({
  HomePage: () => <div data-testid="home-page">Accueil</div>,
}))

vi.mock('@/app/pages/select-shop/SelectShopPage', () => ({
  SelectShopPage: () => <div data-testid="select-shop-page">Sélection boutique</div>,
}))

vi.mock('@/app/pages/SelectUserPage', () => ({
  default: () => <div data-testid="select-user-page">Sélection utilisateur</div>,
}))

vi.mock('@/app/pages/inventory/InventoryLayout', () => ({
  InventoryLayout: () => <div data-testid="inventory-layout" />, 
}))

vi.mock('@/app/pages/inventory/InventoryLocationStep', () => ({
  InventoryLocationStep: () => <div data-testid="inventory-location-step" />,
}))

vi.mock('@/app/pages/inventory/InventoryCountTypeStep', () => ({
  InventoryCountTypeStep: () => <div data-testid="inventory-count-type-step" />, 
}))

vi.mock('@/app/pages/inventory/InventorySessionPage', () => ({
  InventorySessionPage: () => <div data-testid="inventory-session-page" />, 
}))

vi.mock('@/app/pages/admin/AdminLayout', () => ({
  AdminLayout: () => <div data-testid="admin-layout" />, 
}))

vi.mock('@/app/pages/admin/AdminLocationsPage', () => ({
  AdminLocationsPage: () => <div data-testid="admin-locations" />, 
}))

// Les mocks ci-dessus doivent être déclarés avant d'importer AppRoutes
import { AppRoutes } from '@/App'

describe('AppRoutes', () => {
  beforeEach(() => {
    useShopMock.mockReset()
    sessionStorage.clear()
  })

  it('redirige vers la page de sélection quand aucune boutique n’est définie', async () => {
    useShopMock.mockReturnValue(
      createUseShopValue({
        shop: null,
        setShop: vi.fn() as unknown as UseShopValue['setShop'],
        isLoaded: true,
      }),
    )

    render(
      <MemoryRouter initialEntries={['/']}>
        <AppRoutes />
      </MemoryRouter>,
    )

    expect(await screen.findByTestId('select-shop-page')).toBeInTheDocument()
  })

  it('affiche la page d’accueil quand une boutique est disponible', async () => {
    const shop: Shop = { id: 'shop-1', name: 'Boutique 1' }
    useShopMock.mockReturnValue(
      createUseShopValue({
        shop,
        setShop: vi.fn() as unknown as UseShopValue['setShop'],
        isLoaded: true,
      }),
    )
    sessionStorage.setItem(`${SELECTED_USER_STORAGE_PREFIX}.${shop.id}`, JSON.stringify({ userId: 'user-1' }))

    const seenPaths: string[] = []

    const LocationTracker = () => {
      const location = useLocation()
      useEffect(() => {
        seenPaths.push(location.pathname)
      }, [location])
      return null
    }

    render(
      <MemoryRouter initialEntries={['/']}>
        <LocationTracker />
        <AppRoutes />
      </MemoryRouter>,
    )

    expect(await screen.findByTestId('home-page')).toBeInTheDocument()
    expect(seenPaths.at(-1)).toBe('/')
  })

  it('affiche le chargement tant que la boutique n’est pas initialisée', async () => {
    useShopMock.mockReturnValue(
      createUseShopValue({
        shop: null,
        setShop: vi.fn() as unknown as UseShopValue['setShop'],
        isLoaded: false,
      }),
    )

    render(
      <MemoryRouter initialEntries={['/']}>
        <AppRoutes />
      </MemoryRouter>,
    )

    expect(
      await screen.findByText('Chargement de votre boutique…'),
    ).toBeInTheDocument()
  })

  it('redirige vers l’identification quand aucun utilisateur n’est mémorisé', async () => {
    const shop: Shop = { id: 'shop-2', name: 'Boutique 2' }
    useShopMock.mockReturnValue(
      createUseShopValue({
        shop,
        setShop: vi.fn() as unknown as UseShopValue['setShop'],
        isLoaded: true,
      }),
    )

    render(
      <MemoryRouter initialEntries={['/']}>
        <AppRoutes />
      </MemoryRouter>,
    )

    expect(await screen.findByTestId('select-user-page')).toBeInTheDocument()
  })
})
