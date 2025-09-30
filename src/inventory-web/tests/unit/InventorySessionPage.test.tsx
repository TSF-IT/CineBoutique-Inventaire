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

vi.mock('../../src/app/api/inventoryApi', () => ({
  fetchProductByEan: (...args: unknown[]) => fetchProductByEanMock(...args),
  completeInventoryRun: vi.fn(),
}))

vi.mock('../../src/app/contexts/InventoryContext', () => ({
  useInventory: () => ({
    selectedUser: 'Alice',
    countType: 1,
    location: {
      id: '11111111-1111-4111-8111-111111111111',
      code: 'B1',
      label: 'Zone B1',
      isBusy: false,
      busyBy: null,
      activeRunId: null,
      activeCountType: null,
      activeStartedAtUtc: null,
      countStatuses: [
        { countType: 1, status: 'not_started', runId: null, operatorDisplayName: null, startedAtUtc: null, completedAtUtc: null },
        { countType: 2, status: 'not_started', runId: null, operatorDisplayName: null, startedAtUtc: null, completedAtUtc: null },
      ],
    },
    sessionId: null,
    items: [],
    addOrIncrementItem: addOrIncrementItemMock,
    setQuantity: vi.fn(),
    removeItem: vi.fn(),
    setSelectedUser: vi.fn(),
    setCountType: vi.fn(),
    setLocation: vi.fn(),
    setSessionId: vi.fn(),
    reset: vi.fn(),
    clearSession: vi.fn(),
  }),
}))

describe('InventorySessionPage - ajout manuel', () => {
  beforeEach(() => {
    fetchProductByEanMock.mockReset()
    addOrIncrementItemMock.mockReset()
    mockNavigate.mockReset()
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

    const input = screen.getByLabelText(/Scanner/)
    fireEvent.change(input, { target: { value: '12345678\n' } })

    await waitFor(() => {
      expect(fetchProductByEanMock).toHaveBeenCalled()
    })

    const manualButton = screen.getByRole('button', { name: 'Ajouter manuellement' })
    await waitFor(() => {
      expect(manualButton).not.toBeDisabled()
    })

    fireEvent.click(manualButton)

    expect(addOrIncrementItemMock).toHaveBeenCalledWith(
      {
        ean: '12345678',
        name: 'Produit inconnu EAN 12345678',
      },
      { isManual: true },
    )
    expect((input as HTMLInputElement).value).toBe('')
  })
})
