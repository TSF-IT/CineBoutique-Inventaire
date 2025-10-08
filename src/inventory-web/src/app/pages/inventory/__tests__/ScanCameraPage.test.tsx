import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { useEffect, useLayoutEffect, useRef } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { InventoryProvider, useInventory } from '../../../contexts/InventoryContext'
import { ScanCameraPage } from '../ScanCameraPage'
import { CountType, type InventoryItem } from '../../../types/inventory'
import type { ShopUser } from '@/types/user'
import type { Shop } from '@/types/shop'
import type { Location } from '../../../types/inventory'

const fetchProductByEanMock = vi.hoisted(() => vi.fn())
const startInventoryRunMock = vi.hoisted(() => vi.fn())
const scannerCallbacks = vi.hoisted(() => ({
  onDetected: undefined as undefined | ((value: string) => void | Promise<void>),
}))

vi.mock('../../../api/inventoryApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../api/inventoryApi')>()
  return {
    ...actual,
    fetchProductByEan: fetchProductByEanMock,
    startInventoryRun: startInventoryRunMock,
  }
})

vi.mock('../../../components/BarcodeScanner', () => ({
  BarcodeScanner: ({ onDetected }: { onDetected: (value: string) => void | Promise<void> }) => {
    scannerCallbacks.onDetected = onDetected
    return (
      <button type="button" data-testid="mock-barcode-scanner" onClick={() => onDetected('0123456789012')}>
        Simuler un scan
      </button>
    )
  },
}))

const shopMock: Shop = { id: 'shop-1', name: 'CinéBoutique Paris' }
const userMock: ShopUser = {
  id: 'user-1',
  shopId: shopMock.id,
  login: 'user',
  displayName: 'Utilisateur Test',
  isAdmin: false,
  disabled: false,
}

const locationMock: Location = {
  id: 'loc-1',
  code: 'Z01',
  label: 'Zone test',
  isBusy: false,
  busyBy: null,
  activeRunId: null,
  activeCountType: null,
  activeStartedAtUtc: null,
  countStatuses: [],
}

vi.mock('@/state/ShopContext', () => ({
  useShop: () => ({ shop: shopMock, isLoaded: true, setShop: vi.fn() }),
}))

type ItemSeed = { ean: string; name: string; quantity?: number }

const InventoryInitializer = ({ children, initialItems = [], countType = CountType.Count1 }: {
  children: ReactNode
  initialItems?: ItemSeed[]
  countType?: CountType
}) => {
  const { setSelectedUser, setLocation, setCountType, clearSession, addOrIncrementItem, setQuantity } = useInventory()

  useLayoutEffect(() => {
    clearSession()
    setSelectedUser(userMock)
    setLocation(locationMock)
    setCountType(countType)
  }, [clearSession, countType, setCountType, setLocation, setSelectedUser])

  useEffect(() => {
    initialItems.forEach((item) => {
      addOrIncrementItem({ ean: item.ean, name: item.name })
      if (item.quantity && item.quantity > 1) {
        setQuantity(item.ean, item.quantity)
      }
    })
  }, [addOrIncrementItem, initialItems, setQuantity])

  return <>{children}</>
}

const renderScanCameraPage = (options?: {
  items?: ItemSeed[]
  countType?: CountType
  onItemsChange?: (items: InventoryItem[]) => void
}) => {
  const onItemsChange = options?.onItemsChange
  const ItemsObserver = () => {
    const { items } = useInventory()
    const handlerRef = useRef(onItemsChange)
    handlerRef.current = onItemsChange

    useEffect(() => {
      handlerRef.current?.(items)
    }, [items])
    return null
  }

  return render(
    <MemoryRouter initialEntries={[{ pathname: '/inventory/scan-camera' }]}> 
      <InventoryProvider>
        <InventoryInitializer initialItems={options?.items} countType={options?.countType}>
          <ItemsObserver />
          <ScanCameraPage />
        </InventoryInitializer>
      </InventoryProvider>
    </MemoryRouter>,
  )
}

const scrollToMock = vi.fn()
const scrollIntoViewMock = vi.fn()
const originalScrollTo = HTMLElement.prototype.scrollTo
const originalScrollIntoView = HTMLElement.prototype.scrollIntoView

beforeAll(() => {
  Object.defineProperty(HTMLElement.prototype, 'scrollTo', {
    configurable: true,
    value: scrollToMock,
  })
  Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', {
    configurable: true,
    value: scrollIntoViewMock,
  })
})

afterEach(() => {
  scrollToMock.mockClear()
  scrollIntoViewMock.mockClear()
  cleanup()
})

afterAll(() => {
  Object.defineProperty(HTMLElement.prototype, 'scrollTo', {
    configurable: true,
    value: originalScrollTo,
  })
  if (originalScrollIntoView) {
    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', {
      configurable: true,
      value: originalScrollIntoView,
    })
  } else {
    // eslint-disable-next-line @typescript-eslint/no-dynamic-delete
    delete HTMLElement.prototype.scrollIntoView
  }
})

describe('ScanCameraPage', () => {
  beforeEach(() => {
    fetchProductByEanMock.mockReset()
    startInventoryRunMock.mockReset()
    scannerCallbacks.onDetected = undefined
  })

  it('affiche uniquement les trois derniers articles en mode fermé', async () => {
    renderScanCameraPage({
      items: [
        { ean: '1000000000001', name: 'Article 1' },
        { ean: '1000000000002', name: 'Article 2' },
        { ean: '1000000000003', name: 'Article 3' },
        { ean: '1000000000004', name: 'Article 4' },
        { ean: '1000000000005', name: 'Article 5' },
      ],
    })

    const sheet = await screen.findByTestId('scan-sheet')
    expect(sheet).toHaveAttribute('data-state', 'closed')
    const rows = await screen.findAllByTestId('scanned-row')
    expect(rows).toHaveLength(3)
    const labels = rows.map((row) => within(row).getByText(/Article/).textContent)
    expect(labels).toEqual(['Article 3', 'Article 4', 'Article 5'])
  })

  it('met à jour la quantité via les boutons plus et moins', async () => {
    let latestItems: InventoryItem[] = []
    renderScanCameraPage({
      items: [{ ean: '2000000000001', name: 'Produit test', quantity: 2 }],
      onItemsChange: (items) => {
        latestItems = items
      },
    })

    const row = await screen.findByTestId('scanned-row')
    const addButton = within(row).getByRole('button', { name: /augmenter la quantité/i })
    const removeButton = within(row).getByRole('button', { name: /diminuer la quantité/i })
    const input = within(row).getByRole('textbox', { name: /quantité/i })

    await userEvent.click(addButton)
    await waitFor(() => expect(latestItems[0]?.quantity).toBe(3))
    expect(input).toHaveValue('3')

    await userEvent.click(removeButton)
    await waitFor(() => expect(latestItems[0]?.quantity).toBe(2))
    expect(input).toHaveValue('2')
  })

  it('permet la saisie directe de la quantité', async () => {
    let latestItems: InventoryItem[] = []
    renderScanCameraPage({
      items: [{ ean: '3000000000001', name: 'Produit manuel', quantity: 1 }],
      onItemsChange: (items) => {
        latestItems = items
      },
    })

    const row = await screen.findByTestId('scanned-row')
    const input = within(row).getByRole('textbox', { name: /quantité/i })

    await userEvent.clear(input)
    await userEvent.type(input, '12')
    expect(input).toHaveValue('12')
    fireEvent.blur(input)

    await waitFor(() => expect(latestItems[0]?.quantity).toBe(12))
  })

  it('change d’état via un geste de balayage', async () => {
    renderScanCameraPage({
      items: [
        { ean: '4000000000001', name: 'A' },
        { ean: '4000000000002', name: 'B' },
        { ean: '4000000000003', name: 'C' },
        { ean: '4000000000004', name: 'D' },
        { ean: '4000000000005', name: 'E' },
      ],
    })

    let handle: HTMLElement | null = null
    await waitFor(() => {
      handle = document.querySelector('[data-testid="scan-sheet-handle"]')
      if (!handle) {
        throw new Error('Handle not mounted')
      }
    })

    const toggle = within(handle!).getByRole('button', { name: /changer la hauteur du panneau/i })
    await userEvent.click(toggle)
    const sheet = await screen.findByTestId('scan-sheet')
    await waitFor(() => expect(sheet).toHaveAttribute('data-state', 'half'))

    await userEvent.click(toggle)
    await waitFor(() => expect(sheet).toHaveAttribute('data-state', 'full'))

    const allRows = screen.getAllByTestId('scanned-row')
    expect(allRows).toHaveLength(5)
  })

  it('ajoute un article lors d’une détection simulée', async () => {
    fetchProductByEanMock.mockResolvedValue({ ean: '9876543210987', name: 'Produit détecté' })
    startInventoryRunMock.mockResolvedValue({ runId: 'run-1' })

    let latestItems: InventoryItem[] = []
    renderScanCameraPage({
      onItemsChange: (items) => {
        latestItems = items
      },
    })

    await waitFor(() => expect(typeof scannerCallbacks.onDetected).toBe('function'))
    await scannerCallbacks.onDetected?.('0123456789012')

    await waitFor(() => expect(fetchProductByEanMock).toHaveBeenCalledWith('0123456789012'))
    expect(startInventoryRunMock).toHaveBeenCalled()
    await waitFor(() => expect(latestItems.some((item) => item.product.ean === '9876543210987')).toBe(true))
    expect(await screen.findByText('Produit détecté')).toBeInTheDocument()
  })

  it('permet d’ajouter manuellement un produit introuvable après un scan', async () => {
    fetchProductByEanMock.mockRejectedValue({ status: 404 })
    startInventoryRunMock.mockResolvedValue({ runId: 'run-manual' })

    let latestItems: InventoryItem[] = []
    renderScanCameraPage({
      onItemsChange: (items) => {
        latestItems = items
      },
    })

    await waitFor(() => expect(typeof scannerCallbacks.onDetected).toBe('function'))
    await scannerCallbacks.onDetected?.('0123456789012')

    const error = await screen.findByText('Aucun produit trouvé pour 0123456789012.')
    expect(error).toBeInTheDocument()

    const manualButton = await screen.findByRole('button', { name: /ajouter manuellement/i })
    await userEvent.click(manualButton)

    await waitFor(() => expect(latestItems.some((item) => item.product.ean === '0123456789012')).toBe(true))
    await waitFor(() => expect(screen.queryByText(/Aucun produit trouvé/)).not.toBeInTheDocument())
    expect(await screen.findByText('Produit inconnu EAN 0123456789012')).toBeInTheDocument()
  })
})

