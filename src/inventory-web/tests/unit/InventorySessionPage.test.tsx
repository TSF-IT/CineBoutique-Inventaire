import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'

import { InventorySessionPage } from '../../src/app/pages/inventory/InventorySessionPage'

const mockNavigate = vi.fn()
const addOrIncrementItemMock = vi.fn()

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom')
  return {
    ...actual,
    useNavigate: () => mockNavigate,
  }
})

vi.mock('../../src/app/components/BarcodeScanner', () => ({
  BarcodeScanner: () => null,
}))

const fetchProductByEanMock = vi.fn()
const startInventoryRunMock = vi.fn()
const releaseInventoryRunMock = vi.fn()

const mockShopUser = {
  id: 'user-test',
  shopId: 'shop-test',
  login: 'utilisateur-test',
  displayName: 'Utilisateur Test',
  isAdmin: false,
  disabled: false,
} as const

const defaultLocation = {
  id: '11111111-1111-4111-8111-111111111111',
  code: 'B1',
  label: 'Zone B1',
  isBusy: false,
  busyBy: null,
  activeRunId: null,
  activeCountType: null,
  activeStartedAtUtc: null,
  countStatuses: [
    {
      countType: 1,
      status: 'not_started',
      runId: null,
      ownerDisplayName: null,
      ownerUserId: null,
      startedAtUtc: null,
      completedAtUtc: null,
    },
    {
      countType: 2,
      status: 'not_started',
      runId: null,
      ownerDisplayName: null,
      ownerUserId: null,
      startedAtUtc: null,
      completedAtUtc: null,
    },
  ],
} as const

const logEventMock = vi.fn()

const defaultInventoryContext = {
  selectedUser: mockShopUser,
  countType: 1,
  location: defaultLocation,
  sessionId: null,
  items: [] as Array<{ product: { ean: string; name: string }; quantity: number }>,
  addOrIncrementItem: addOrIncrementItemMock,
  setQuantity: vi.fn(),
  removeItem: vi.fn(),
  setSelectedUser: vi.fn(),
  setCountType: vi.fn(),
  setLocation: vi.fn(),
  setSessionId: vi.fn(),
  reset: vi.fn(),
  clearSession: vi.fn(),
  logs: [] as Array<unknown>,
  logEvent: logEventMock,
  clearLogs: vi.fn(),
}

type InventoryContextMock = typeof defaultInventoryContext

let inventoryStateOverride: Partial<InventoryContextMock> = {}

vi.mock('../../src/app/api/inventoryApi', () => ({
  fetchProductByEan: (...args: unknown[]) => fetchProductByEanMock(...args),
  completeInventoryRun: vi.fn(),
  startInventoryRun: (...args: unknown[]) => startInventoryRunMock(...args),
  releaseInventoryRun: (...args: unknown[]) => releaseInventoryRunMock(...args),
}))

vi.mock('../../src/state/ShopContext', () => ({
  useShop: () => ({ shop: { id: mockShopUser.shopId, name: 'Boutique Test' }, setShop: vi.fn(), isLoaded: true }),
}))

vi.mock('../../src/app/contexts/InventoryContext', () => ({
  useInventory: () => ({
    ...defaultInventoryContext,
    ...inventoryStateOverride,
  }),
}))

describe('InventorySessionPage - ajout manuel', () => {
  beforeEach(() => {
    fetchProductByEanMock.mockReset()
    addOrIncrementItemMock.mockReset()
    logEventMock.mockReset()
    mockNavigate.mockReset()
    startInventoryRunMock.mockReset()
    startInventoryRunMock.mockResolvedValue({
      runId: 'mock-run',
      inventorySessionId: 'mock-session',
      locationId: '11111111-1111-4111-8111-111111111111',
      countType: 1,
      ownerDisplayName: mockShopUser.displayName,
      ownerUserId: mockShopUser.id,
      startedAtUtc: new Date().toISOString(),
    })
    releaseInventoryRunMock.mockReset()
    releaseInventoryRunMock.mockResolvedValue(undefined)
    inventoryStateOverride = {}
  })

  it("ajoute immédiatement un produit inconnu lorsqu’un code non référencé est validé manuellement", async () => {
    const httpError = Object.assign(new Error('HTTP 404'), {
      status: 404,
      url: 'http://localhost/api/products/12345678',
      body: 'Not Found',
      problem: undefined,
    })
    fetchProductByEanMock.mockRejectedValue(httpError)

    render(<InventorySessionPage />)

    const [input] = screen.getAllByLabelText(/Scanner/)
    fireEvent.change(input, { target: { value: '12345678' } })
    fireEvent.keyDown(input, { key: 'Enter' })

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Ajouter manuellement' })).not.toBeDisabled()
    })

    fireEvent.click(screen.getByRole('button', { name: 'Ajouter manuellement' }))

    await waitFor(() => {
      expect(startInventoryRunMock).toHaveBeenCalled()
      expect(addOrIncrementItemMock).toHaveBeenCalledWith(
        {
          sku: '',
          ean: '12345678',
          name: 'Produit inconnu EAN 12345678',
        },
        { isManual: true },
      )
    })
    expect(logEventMock).not.toHaveBeenCalled()
    expect((input as HTMLInputElement).value).toBe('')
  })

  it("n'initialise pas de comptage lorsqu'aucune zone n'est sélectionnée", async () => {
    inventoryStateOverride = { location: null }

    render(<InventorySessionPage />)

    const [input] = screen.getAllByLabelText(/Scanner/)
    fireEvent.change(input, { target: { value: '1234567890123' } })
    fireEvent.keyDown(input, { key: 'Enter' })

    await waitFor(() => {
      expect(screen.getByText('Sélectionnez une zone avant de scanner un produit.')).toBeInTheDocument()
    })

    expect(startInventoryRunMock).not.toHaveBeenCalled()
    expect(fetchProductByEanMock).not.toHaveBeenCalled()
  })
})
