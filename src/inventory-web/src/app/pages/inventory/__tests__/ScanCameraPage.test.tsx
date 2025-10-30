import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { useEffect, useLayoutEffect, useRef } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { InventoryProvider, useInventory } from '../../../contexts/InventoryContext'
import { CountType, type InventoryItem, type Location } from '../../../types/inventory'
import { ScanCameraPage } from '../ScanCameraPage'

import type { Shop } from '@/types/shop'
import type { ShopUser } from '@/types/user'

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

const shopMock: Shop = { id: 'shop-1', name: 'CinéBoutique Paris', kind: 'boutique' }
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
  disabled: false,
}

vi.mock('@/state/ShopContext', () => ({
  useShop: () => ({ shop: shopMock, isLoaded: true, setShop: vi.fn() }),
}))

type ItemSeed = { ean: string; name: string; quantity?: number }

const InventoryInitializer = ({
  children,
  initialItems = [],
  countType = CountType.Count1,
}: {
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

    useEffect(() => {
      handlerRef.current = onItemsChange
    })

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

afterEach(() => {
  fetchProductByEanMock.mockReset()
  startInventoryRunMock.mockReset()
  scannerCallbacks.onDetected = undefined
  cleanup()
})

describe('ScanCameraPage', () => {
  it('affiche les informations de la zone et la quantité totale', async () => {
    renderScanCameraPage({
      items: [
        { ean: '1000000000001', name: 'Article 1' },
        { ean: '1000000000002', name: 'Article 2' },
      ],
    })

    expect(await screen.findByText('CinéBoutique Paris')).toBeInTheDocument()
    expect(screen.getByText('Zone test')).toBeInTheDocument()
    expect(screen.getByText('2 pièces')).toBeInTheDocument()
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
    const increment = within(row).getByRole('button', { name: /augmenter/i })
    const decrement = within(row).getByRole('button', { name: /diminuer/i })
    const input = within(row).getByRole('textbox', { name: /quantité/i })

    await userEvent.click(increment)
    await waitFor(() => expect(input).toHaveValue('3'))
    expect(latestItems[0]?.quantity).toBe(3)

    await userEvent.click(decrement)
    await waitFor(() => expect(input).toHaveValue('2'))
    expect(latestItems[0]?.quantity).toBe(2)
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

  it('ajoute un article lors d’une détection simulée', async () => {
    fetchProductByEanMock.mockResolvedValue({
      ean: '9876543210987',
      name: 'Produit détecté',
      sku: 'SKU-987654',
      subGroup: 'Goodies',
    })
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
    const row = await screen.findByTestId('scanned-row')
    expect(within(row).getByText('Produit détecté')).toBeInTheDocument()
    expect(within(row).getByText(/Sous-groupe Goodies/)).toBeInTheDocument()
  })

  it('normalise les codes alphanumériques sans filtrer les lettres', async () => {
    fetchProductByEanMock.mockResolvedValue({ ean: 'RFID-123A', name: 'Badge RFID' })
    startInventoryRunMock.mockResolvedValue({ runId: 'run-2' })

    let latestItems: InventoryItem[] = []
    renderScanCameraPage({
      onItemsChange: (items) => {
        latestItems = items
      },
    })

    await waitFor(() => expect(typeof scannerCallbacks.onDetected).toBe('function'))
    await scannerCallbacks.onDetected?.(' rf id-123a ')

    await waitFor(() => expect(fetchProductByEanMock).toHaveBeenCalledWith(' rf id-123a '))
    await waitFor(() => expect(startInventoryRunMock).toHaveBeenCalled())
    await waitFor(() => expect(latestItems.some((item) => item.product.ean === 'RFID-123A')).toBe(true))
    expect(await screen.findByText('Badge RFID')).toBeInTheDocument()
  })

  it("signale l'absence de résultat sans proposer d'ajout manuel", async () => {
    fetchProductByEanMock.mockRejectedValue({ status: 404 })

    let latestItems: InventoryItem[] = []
    renderScanCameraPage({
      onItemsChange: (items) => {
        latestItems = items
      },
    })

    await waitFor(() => expect(typeof scannerCallbacks.onDetected).toBe('function'))
    await scannerCallbacks.onDetected?.('0123456789012')

    await waitFor(() =>
      expect(
        screen.getByText('Code 0123456789012 introuvable dans la liste des produits à inventorier.'),
      ).toBeInTheDocument(),
    )
    expect(latestItems.some((item) => item.product.ean === '0123456789012')).toBe(false)
  })

  it('ignore les lectures répétées du même code tant que la caméra le voit', async () => {
    fetchProductByEanMock.mockResolvedValue({ ean: '5555555555555', name: 'Bonbon' })
    startInventoryRunMock.mockResolvedValue({ runId: 'run-lock' })

    let latestItems: InventoryItem[] = []
    renderScanCameraPage({
      onItemsChange: (items) => {
        latestItems = items
      },
    })

    await waitFor(() => expect(typeof scannerCallbacks.onDetected).toBe('function'))

    await scannerCallbacks.onDetected?.('0123456789012')
    await waitFor(() => expect(fetchProductByEanMock).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(latestItems[0]?.quantity).toBe(1))

    await scannerCallbacks.onDetected?.('0123456789012')
    expect(fetchProductByEanMock).toHaveBeenCalledTimes(1)

    await new Promise((resolve) => setTimeout(resolve, 800))
    await scannerCallbacks.onDetected?.('0123456789012')
    await waitFor(() => expect(fetchProductByEanMock).toHaveBeenCalledTimes(2))
  })
})
