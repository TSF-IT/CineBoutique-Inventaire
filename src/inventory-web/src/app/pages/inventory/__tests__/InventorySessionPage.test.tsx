import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { useEffect, useLayoutEffect } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { InventoryProvider, useInventory } from '../../../contexts/InventoryContext'
import { InventorySessionPage } from '../InventorySessionPage'
import { CountType } from '../../../types/inventory'
import type { Location } from '../../../types/inventory'
import type { ShopUser } from '@/types/user'
import type { Shop } from '@/types/shop'
import type { HttpError } from '@/lib/api/http'

const getConflictZoneDetailMock = vi.hoisted(() => vi.fn())
const fetchProductByEanMock = vi.hoisted(() => vi.fn())
const scannerCallbacks = vi.hoisted(() => ({
  onDetected: undefined as undefined | ((value: string) => Promise<void> | void),
}))

const { shopMock } = vi.hoisted(() => ({
  shopMock: { id: 'shop-test', name: 'Boutique test' } as Shop,
}))

const inventoryControls: { setCountType?: (type: number | null) => void } = {}

vi.mock('../../../api/inventoryApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../api/inventoryApi')>()
  return {
    ...actual,
    getConflictZoneDetail: getConflictZoneDetailMock,
    fetchProductByEan: fetchProductByEanMock,
  }
})

vi.mock('../../../components/BarcodeScanner', () => ({
  BarcodeScanner: ({ onDetected }: { onDetected: (value: string) => Promise<void> | void }) => {
    scannerCallbacks.onDetected = onDetected
    return (
      <button
        type="button"
        data-testid="mock-barcode-scanner"
        onClick={() => {
          void onDetected('0123456789012')
        }}
      >
        Simuler un scan
      </button>
    )
  },
}))

vi.mock('@/state/ShopContext', () => ({
  useShop: () => ({ shop: shopMock, setShop: vi.fn(), isLoaded: true }),
}))

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

const renderSessionPage = (countType: CountType) => {
  return render(
    <MemoryRouter initialEntries={[{ pathname: '/inventory/session' }]}> 
      <InventoryProvider>
        <InventoryStateInitializer location={baseLocation} countType={countType}>
          <InventorySessionPage />
        </InventoryStateInitializer>
      </InventoryProvider>
    </MemoryRouter>,
  )
}

describe('InventorySessionPage - conflits', () => {
  beforeEach(() => {
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
})

describe('InventorySessionPage - caméra', () => {
  beforeEach(() => {
    fetchProductByEanMock.mockReset()
    fetchProductByEanMock.mockRejectedValue({
      name: 'HttpError',
      message: 'Not found',
      status: 404,
      url: '/api/products/ean',
    } as HttpError)
    scannerCallbacks.onDetected = undefined
  })

  it('copie le code détecté dans le champ de scan et scroll vers celui-ci si nécessaire', async () => {
    const user = userEvent.setup()
    renderSessionPage(CountType.Count1)

    const [toggleCameraButton] = await screen.findAllByRole('button', { name: /activer la caméra/i })
    await user.click(toggleCameraButton)

    const [scanInput] = await screen.findAllByLabelText('Scanner (douchette ou saisie)')
    const scrollIntoViewMock = vi.fn()
    Object.defineProperty(scanInput, 'scrollIntoView', {
      value: scrollIntoViewMock,
      configurable: true,
    })

    const getBoundingClientRectSpy = vi
      .spyOn(scanInput, 'getBoundingClientRect')
      .mockReturnValue({
        top: 1200,
        bottom: 1300,
        left: 0,
        right: 0,
        width: 0,
        height: 0,
        x: 0,
        y: 0,
        toJSON: () => ({}),
      } as DOMRect)

    await act(async () => {
      const handler = scannerCallbacks.onDetected
      if (handler) {
        await handler('  9876543210987  ')
      }
    })

    await waitFor(() => expect(scanInput).toHaveValue('9876543210987'))
    expect(scrollIntoViewMock).toHaveBeenCalledWith({ behavior: 'smooth', block: 'center' })
    await waitFor(() => expect(fetchProductByEanMock).toHaveBeenCalledTimes(1))

    getBoundingClientRectSpy.mockRestore()
    // Nettoyage de la surcharge scrollIntoView pour les autres tests
    delete (scanInput as HTMLElement).scrollIntoView
  })
})
