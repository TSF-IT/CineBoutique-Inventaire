import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { useEffect, useLayoutEffect } from 'react'
import { MemoryRouter } from 'react-router-dom'
import * as ReactRouterDom from 'react-router-dom'
import { afterAll, beforeEach, describe, expect, it, vi } from 'vitest'

import * as inventoryApi from '../../../api/inventoryApi'
import { InventoryProvider, useInventory, type InventoryContextValue } from '../../../contexts/InventoryContext'
import { CountType } from '../../../types/inventory'
import type { InventoryItem, Location, Product } from '../../../types/inventory'
import { InventorySessionPage, aggregateItemsForCompletion } from '../InventorySessionPage'

import { ShopProvider, useShop } from '@/state/ShopContext'
import type { Shop } from '@/types/shop'
import type { ShopUser } from '@/types/user'

const getConflictZoneDetailMock = vi.spyOn(inventoryApi, 'getConflictZoneDetail')
const fetchProductByEanMock = vi.spyOn(inventoryApi, 'fetchProductByEan')
const completeInventoryRunMock = vi.spyOn(inventoryApi, 'completeInventoryRun')
const shopMock: Shop = { id: 'shop-test', name: 'Boutique test', kind: 'boutique' }

const inventoryControls: Partial<Pick<InventoryContextValue, 'setCountType' | 'initializeItems'>> = {}

afterAll(() => {
  getConflictZoneDetailMock.mockRestore()
  fetchProductByEanMock.mockRestore()
  completeInventoryRunMock.mockRestore()
})

const owner: ShopUser = {
  id: 'user-test',
  shopId: shopMock.id,
  login: 'user.test',
  displayName: 'Utilisateur Test',
  isAdmin: false,
  disabled: false,
}

const baseLocation: Location = {
  id: 'location-test',
  code: 'Z01',
  label: 'Zone test',
  disabled: false,
  isBusy: false,
  busyBy: null,
  activeRunId: null,
  activeCountType: null,
  activeStartedAtUtc: null,
  countStatuses: [
    {
      countType: 1,
      status: 'completed',
      runId: 'run-c1',
      ownerDisplayName: 'Utilisateur 1',
      ownerUserId: 'user-c1',
      startedAtUtc: null,
      completedAtUtc: null,
    },
    {
      countType: 2,
      status: 'completed',
      runId: 'run-c2',
      ownerDisplayName: 'Utilisateur 2',
      ownerUserId: 'user-c2',
      startedAtUtc: null,
      completedAtUtc: null,
    },
    {
      countType: 3,
      status: 'not_started',
      runId: null,
      ownerDisplayName: null,
      ownerUserId: null,
      startedAtUtc: null,
      completedAtUtc: null,
    },
  ],
}

const InventoryStateInitializer = ({
  children,
  location,
  countType,
}: {
  children: ReactNode
  location: Location
  countType: CountType
}) => {
  const { setSelectedUser, setLocation, setCountType, clearSession, initializeItems } = useInventory()
  useLayoutEffect(() => {
    clearSession()
    setSelectedUser(owner)
    setLocation(location)
    setCountType(countType)
  }, [clearSession, countType, location, setCountType, setLocation, setSelectedUser])

  useEffect(() => {
    setCountType(countType)
  }, [countType, setCountType])

  useEffect(() => {
    inventoryControls.setCountType = setCountType
    inventoryControls.initializeItems = initializeItems
    return () => {
      inventoryControls.setCountType = undefined
      inventoryControls.initializeItems = undefined
    }
  }, [initializeItems, setCountType])

  return <>{children}</>
}

const ShopInitializer = ({ shop }: { shop: Shop }) => {
  const { setShop } = useShop()

  useEffect(() => {
    setShop(shop)
  }, [setShop, shop])

  return null
}

const LocationObserver = () => {
  const location = ReactRouterDom.useLocation()
  return <span data-testid="current-path">{location.pathname}</span>
}

const renderSessionPage = (countType: CountType) => {
  localStorage.setItem('cb.shop', JSON.stringify(shopMock))
  return render(
    <MemoryRouter initialEntries={[{ pathname: '/inventory/session' }]}>
      <ShopProvider>
        <ShopInitializer shop={shopMock} />
        <InventoryProvider>
          <InventoryStateInitializer location={baseLocation} countType={countType}>
            <InventorySessionPage />
          </InventoryStateInitializer>
        </InventoryProvider>
      </ShopProvider>
      <LocationObserver />
    </MemoryRouter>,
  )
}

describe('InventorySessionPage - conflits', () => {
  beforeEach(() => {
    fetchProductByEanMock.mockReset()
    getConflictZoneDetailMock.mockReset()
    completeInventoryRunMock.mockReset()
    getConflictZoneDetailMock.mockResolvedValue({
      locationId: baseLocation.id,
      locationCode: baseLocation.code,
      locationLabel: baseLocation.label,
      items: [],
    })
  })

  it('affiche un accès aux écarts pour un troisième comptage', async () => {
    renderSessionPage(CountType.Count3)

    expect(await screen.findByTestId('btn-view-conflicts')).toBeInTheDocument()
  })

  it('ouvre le détail des écarts sur demande', async () => {
    renderSessionPage(CountType.Count3)

    const [button] = await screen.findAllByTestId('btn-view-conflicts')
    fireEvent.click(button)

    await waitFor(() => expect(getConflictZoneDetailMock).toHaveBeenCalled())
    expect(
      await screen.findByRole('dialog', {
        name: `${baseLocation.code} · ${baseLocation.label}`,
      }),
    ).toBeInTheDocument()
  })

  it('masque les options de scan lors du troisième comptage', async () => {
    renderSessionPage(CountType.Count3)

    expect(await screen.findByText('Références en conflit')).toBeInTheDocument()
    expect(screen.queryByLabelText(/Scanner \(douchette ou saisie\)/i)).not.toBeInTheDocument()
    expect(screen.queryByTestId('btn-open-manual')).not.toBeInTheDocument()
    expect(screen.queryByTestId('btn-scan-camera')).not.toBeInTheDocument()
  })

  it('préremplit les références en conflit avec une quantité à zéro', async () => {
    getConflictZoneDetailMock.mockResolvedValue({
      locationId: baseLocation.id,
      locationCode: baseLocation.code,
      locationLabel: baseLocation.label,
      items: [
        { productId: 'prod-1', ean: '0123456789012', qtyC1: 10, qtyC2: 8, delta: 2, sku: 'SKU-1' },
        { productId: 'prod-2', ean: '0001112223334', qtyC1: 5, qtyC2: 6, delta: -1, sku: 'SKU-2' },
      ],
    })
    fetchProductByEanMock.mockImplementation(async (ean) => ({
      ean,
      name: `Produit ${ean}`,
      sku: `SKU-${ean}`,
    }))

    renderSessionPage(CountType.Count3)

    await waitFor(() => expect(getConflictZoneDetailMock).toHaveBeenCalled())
    expect(await screen.findByText('Produit 0123456789012')).toBeInTheDocument()
    expect(await screen.findByText('Produit 0001112223334')).toBeInTheDocument()

    const inputs = await screen.findAllByTestId('quantity-input')
    expect(inputs).toHaveLength(2)
    expect(inputs[0]).toHaveValue('0')
    expect(inputs[1]).toHaveValue('0')

    const completeButton = await screen.findByTestId('btn-complete-run')
    expect(completeButton).toBeDisabled()

    fireEvent.change(inputs[0], { target: { value: '5' } })
    expect(completeButton).not.toBeDisabled()
  })

  it('ignore les doublons de références en conflit', async () => {
    getConflictZoneDetailMock.mockResolvedValue({
      locationId: baseLocation.id,
      locationCode: baseLocation.code,
      locationLabel: baseLocation.label,
      items: [
        { productId: 'prod-1', ean: '185323000132', qtyC1: 3, qtyC2: 5, delta: -2, sku: 'SKU-ROLLBAR' },
        { productId: 'prod-1', ean: '185323000132', qtyC1: 3, qtyC2: 5, delta: -2, sku: 'SKU-ROLLBAR' },
        { productId: 'prod-2', ean: '185323000132', qtyC1: 1, qtyC2: 0, delta: 1, sku: 'SKU-ROLLBAR' },
      ],
    })

    fetchProductByEanMock.mockResolvedValue({
      ean: '185323000132',
      name: 'Produit Rollbar',
      sku: 'SKU-ROLLBAR',
    })

    renderSessionPage(CountType.Count3)

    await waitFor(() => expect(getConflictZoneDetailMock).toHaveBeenCalled())

    const inputs = await screen.findAllByTestId('quantity-input')
    expect(inputs).toHaveLength(1)
    expect(await screen.findByText('Produit Rollbar')).toBeInTheDocument()
    expect(fetchProductByEanMock).toHaveBeenCalledTimes(1)
  })
})

describe('InventorySessionPage - catalogue', () => {
  beforeEach(() => {
    fetchProductByEanMock.mockReset()
    getConflictZoneDetailMock.mockReset()
  })

  it('permet d’ajouter un produit via le catalogue', async () => {
    const catalogueProduct = {
      id: 'cat-prod-900',
      sku: 'SKU-900',
      ean: '9000000000000',
      name: 'Produit catalogue',
      group: 'Films',
      subGroup: 'Blu-ray',
    }
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ items: [catalogueProduct] }),
    })
    const originalFetch = global.fetch
    global.fetch = fetchMock as unknown as typeof fetch

    fetchProductByEanMock.mockResolvedValue({
      ean: '9000000000000',
      sku: 'SKU-900',
      name: 'Produit catalogue',
      subGroup: 'Blu-ray',
    })

    const user = userEvent.setup()
    renderSessionPage(CountType.Count1)

    try {
      const openButton = await screen.findByTestId('btn-open-catalogue')
      await user.click(openButton)

      const searchInput = await screen.findByPlaceholderText('Rechercher (SKU / EAN / nom)')
      await user.type(searchInput, 'Catalogue')

      await waitFor(() => expect(fetchMock).toHaveBeenCalled())

      const option = await screen.findByTestId('products-modal-row-cat-prod-900')
      await user.click(option)

      await waitFor(() => expect(fetchProductByEanMock).toHaveBeenCalledWith('9000000000000'))
      await waitFor(() => expect(screen.getByText('Produit catalogue')).toBeInTheDocument())
      expect(screen.getByText(/Sous-groupe Blu-ray/)).toBeInTheDocument()
      expect(screen.queryByText('Ajout manuel')).not.toBeInTheDocument()
    } finally {
      global.fetch = originalFetch
    }
  })
})

describe('InventorySessionPage - navigation', () => {
  it('redirige vers la page de scan caméra', async () => {
    const user = userEvent.setup()
    renderSessionPage(CountType.Count1)

    const button = await screen.findByTestId('btn-scan-camera')
    await user.click(button)

    await waitFor(() => {
      const paths = screen.getAllByTestId('current-path').map((element) => element.textContent)
      expect(paths).toContain('/inventory/scan-camera')
    })
  })
})

describe('InventorySessionPage - complétion', () => {
  beforeEach(() => {
    fetchProductByEanMock.mockReset()
    getConflictZoneDetailMock.mockReset()
    completeInventoryRunMock.mockReset()
  })

  it('affiche un message explicite lorsqu’un code contient un caractère interdit', async () => {
    const user = userEvent.setup()
    renderSessionPage(CountType.Count1)

    await waitFor(() => expect(inventoryControls.initializeItems).toBeDefined())

    act(() => {
      inventoryControls.initializeItems?.([
        {
          product: { ean: '@@@', name: 'Produit invalide', sku: 'SKU-INVALID' },
          quantity: 2,
          isManual: true,
        },
      ])
    })

    expect(await screen.findByText('Produit invalide')).toBeInTheDocument()

    const completeButton = await screen.findByTestId('btn-complete-run')
    expect(completeButton).not.toBeDisabled()

    await user.click(completeButton)

    const confirmButton = await screen.findByTestId('btn-confirm-complete')
    await user.click(confirmButton)

    const errorMessage = await screen.findByText(/articles ont un EAN invalide/i)
    expect(errorMessage.textContent).toContain('@@@')
    expect(completeInventoryRunMock).not.toHaveBeenCalled()
  })

  it('autorise la clôture avec un code alphanumérique court', async () => {
    const user = userEvent.setup()
    renderSessionPage(CountType.Count1)

    await waitFor(() => expect(inventoryControls.initializeItems).toBeDefined())

    act(() => {
      inventoryControls.initializeItems?.([
        {
          product: { ean: '2066B', name: 'Produit catalogue', sku: 'SKU-2066B' },
          quantity: 1,
          isManual: false,
        },
      ])
    })

    const completeButton = await screen.findByTestId('btn-complete-run')
    await user.click(completeButton)

    const confirmButton = await screen.findByTestId('btn-confirm-complete')
    await user.click(confirmButton)

    await waitFor(() => expect(completeInventoryRunMock).toHaveBeenCalled())
    const payload = completeInventoryRunMock.mock.calls[0]?.[1]
    expect(payload?.items).toEqual([
      expect.objectContaining({ ean: '2066B', quantity: 1 }),
    ])
  })
})

const createInventoryItem = (
  overrides: Partial<InventoryItem> & { product?: Product },
): InventoryItem => ({
  id: overrides.id ?? `item-${Math.random().toString(36).slice(2)}`,
  product:
    overrides.product ?? {
      ean: '0000000000000',
      name: 'Produit par défaut',
      sku: 'SKU-DEFAULT',
    },
  quantity: overrides.quantity ?? 1,
  lastScanAt: overrides.lastScanAt ?? new Date().toISOString(),
  isManual: overrides.isManual ?? false,
  addedAt: overrides.addedAt ?? Date.now(),
  hasConflict: overrides.hasConflict,
})

describe('aggregateItemsForCompletion', () => {
  it('additionne les quantités pour un même EAN', () => {
    const now = new Date().toISOString()
    const items: InventoryItem[] = [
      createInventoryItem({
        product: { ean: '1234567890123', name: 'Produit 1', sku: 'SKU-1' },
        quantity: 2,
        lastScanAt: now,
        isManual: false,
      }),
      createInventoryItem({
        product: { ean: '1234567890123', name: 'Produit 1', sku: 'SKU-1' },
        quantity: 3,
        lastScanAt: now,
        isManual: true,
      }),
      createInventoryItem({
        product: { ean: '9999999999999', name: 'Produit 2', sku: 'SKU-2' },
        quantity: 1,
        lastScanAt: now,
        isManual: false,
      }),
    ]

    const result = aggregateItemsForCompletion(items)

    expect(result).toEqual([
      { ean: '1234567890123', quantity: 5, isManual: true },
      { ean: '9999999999999', quantity: 1, isManual: false },
    ])
  })

  it("ignore les lignes sans EAN valide ou quantité positive", () => {
    const now = new Date().toISOString()
    const items: InventoryItem[] = [
      createInventoryItem({
        product: { ean: '   ', name: 'Produit invalide', sku: 'SKU-INVALID' },
        quantity: 4,
        lastScanAt: now,
      }),
      createInventoryItem({
        product: { ean: '5555555555555', name: 'Produit 3', sku: 'SKU-3' },
        quantity: 0,
        lastScanAt: now,
      }),
      createInventoryItem({
        product: { ean: '6666666666666', name: 'Produit 4', sku: 'SKU-4' },
        quantity: -2,
        lastScanAt: now,
      }),
      createInventoryItem({
        product: { ean: '2066B', name: 'Produit 5', sku: 'SKU-5' },
        quantity: 3,
        lastScanAt: now,
      }),
    ]

    const result = aggregateItemsForCompletion(items)

    expect(result).toEqual([])
  })

  it('normalise les EAN contenant des séparateurs non numériques', () => {
    const now = new Date().toISOString()
    const items: InventoryItem[] = [
      createInventoryItem({
        product: { ean: '2015\u202f02\u202f810', name: 'Produit 5', sku: 'SKU-5' },
        quantity: 1,
        lastScanAt: now,
      }),
      createInventoryItem({
        product: { ean: '2015-02-810', name: 'Produit 6', sku: 'SKU-6' },
        quantity: 2,
        lastScanAt: now,
      }),
    ]

    const result = aggregateItemsForCompletion(items)

    expect(result).toEqual([{ ean: '201502810', quantity: 3, isManual: false }])
  })
})
