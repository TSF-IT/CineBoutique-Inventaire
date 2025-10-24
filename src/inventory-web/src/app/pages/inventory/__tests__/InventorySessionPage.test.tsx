import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { useEffect, useLayoutEffect } from 'react'
import { MemoryRouter } from 'react-router-dom'
import * as ReactRouterDom from 'react-router-dom'
import { afterAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { InventoryProvider, useInventory } from '../../../contexts/InventoryContext'
import { InventorySessionPage } from '../InventorySessionPage'
import { CountType } from '../../../types/inventory'
import type { Location } from '../../../types/inventory'
import type { ShopUser } from '@/types/user'
import type { Shop } from '@/types/shop'
import { ShopProvider, useShop } from '@/state/ShopContext'
import * as inventoryApi from '../../../api/inventoryApi'

const getConflictZoneDetailMock = vi.spyOn(inventoryApi, 'getConflictZoneDetail')
const fetchProductByEanMock = vi.spyOn(inventoryApi, 'fetchProductByEan')
const shopMock: Shop = { id: 'shop-test', name: 'Boutique test', kind: 'boutique' }

const inventoryControls: { setCountType?: (type: number | null) => void } = {}

afterAll(() => {
  getConflictZoneDetailMock.mockRestore()
  fetchProductByEanMock.mockRestore()
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
  const { setSelectedUser, setLocation, setCountType, clearSession } = useInventory()
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
    return () => {
      inventoryControls.setCountType = undefined
    }
  }, [setCountType])

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
})

describe('InventorySessionPage - navigation', () => {
  it('redirige vers la page de scan caméra', async () => {
    const user = userEvent.setup()
    renderSessionPage(CountType.Count1)

    const [button] = await screen.findAllByRole('button', { name: /scan caméra/i })
    await user.click(button)

    await waitFor(() => {
      const paths = screen.getAllByTestId('current-path').map((element) => element.textContent)
      expect(paths).toContain('/inventory/scan-camera')
    })
  })
})
