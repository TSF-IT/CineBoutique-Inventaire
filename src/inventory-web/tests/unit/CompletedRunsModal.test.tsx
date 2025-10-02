import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, beforeEach, vi, afterEach } from 'vitest'

vi.mock('../../src/app/api/inventoryApi', () => ({
  getCompletedRunDetail: vi.fn(),
}))

import { getCompletedRunDetail } from '../../src/app/api/inventoryApi'
import { CompletedRunsModal } from '../../src/app/components/Runs/CompletedRunsModal'

const mockedGetCompletedRunDetail = vi.mocked(getCompletedRunDetail)

const baseRun = {
  runId: 'run-1',
  locationId: 'loc-1',
  locationCode: 'B1',
  locationLabel: 'Zone B1',
  countType: 1,
  operatorDisplayName: 'Toto',
  startedAtUtc: '2025-12-17T09:00:00Z',
  completedAtUtc: '2025-12-17T10:00:00Z',
}

const baseDetail = {
  runId: 'run-1',
  locationId: 'loc-1',
  locationCode: 'B1',
  locationLabel: 'Zone B1',
  countType: 1,
  operatorDisplayName: 'Toto',
  startedAtUtc: '2025-12-17T09:00:00Z',
  completedAtUtc: '2025-12-17T10:00:00Z',
  items: [
    {
      productId: 'prod-1',
      sku: 'SKU-1',
      name: 'Produit Test',
      ean: '123',
      quantity: 3,
    },
  ],
}

describe('CompletedRunsModal', () => {
  const user = userEvent.setup()
  let createObjectUrlMock: ReturnType<typeof vi.fn>
  let revokeObjectUrlMock: ReturnType<typeof vi.fn>
  let anchorClickMock: ReturnType<typeof vi.fn>
  const originalCreateObjectURL = URL.createObjectURL
  const originalRevokeObjectURL = URL.revokeObjectURL

  beforeEach(() => {
    mockedGetCompletedRunDetail.mockReset()
    mockedGetCompletedRunDetail.mockResolvedValue(baseDetail)

    createObjectUrlMock = vi.fn(() => 'blob:mock')
    revokeObjectUrlMock = vi.fn()
    anchorClickMock = vi.fn()

    Object.defineProperty(URL, 'createObjectURL', {
      configurable: true,
      writable: true,
      value: createObjectUrlMock,
    })
    Object.defineProperty(URL, 'revokeObjectURL', {
      configurable: true,
      writable: true,
      value: revokeObjectUrlMock,
    })
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(anchorClickMock)
  })

  afterEach(() => {
    vi.restoreAllMocks()

    if (originalCreateObjectURL) {
      Object.defineProperty(URL, 'createObjectURL', {
        configurable: true,
        writable: true,
        value: originalCreateObjectURL,
      })
    } else {
      // eslint-disable-next-line @typescript-eslint/no-dynamic-delete -- nettoyage test
      delete (URL as { createObjectURL?: unknown }).createObjectURL
    }

    if (originalRevokeObjectURL) {
      Object.defineProperty(URL, 'revokeObjectURL', {
        configurable: true,
        writable: true,
        value: originalRevokeObjectURL,
      })
    } else {
      // eslint-disable-next-line @typescript-eslint/no-dynamic-delete -- nettoyage test
      delete (URL as { revokeObjectURL?: unknown }).revokeObjectURL
    }
  })

  it('affiche le détail et permet l’export CSV', async () => {
    render(<CompletedRunsModal open completedRuns={[baseRun]} onClose={() => {}} />)

    const openButton = screen.getByRole('button', { name: /B1/i })
    await user.click(openButton)

    await waitFor(() => expect(mockedGetCompletedRunDetail).toHaveBeenCalledWith('run-1'))

    expect(await screen.findByText(/Détail du comptage/)).toBeInTheDocument()
    expect(screen.getByText(/Produit Test/)).toBeInTheDocument()

    const exportButton = screen.getByRole('button', { name: /Exporter en CSV/i })
    await user.click(exportButton)

    expect(createObjectUrlMock).toHaveBeenCalledTimes(1)
    expect(revokeObjectUrlMock).toHaveBeenCalledTimes(1)
    expect(anchorClickMock).toHaveBeenCalledTimes(1)
  })

  it('affiche l’erreur et relance le chargement', async () => {
    mockedGetCompletedRunDetail.mockRejectedValueOnce(new Error('Oups'))

    render(<CompletedRunsModal open completedRuns={[baseRun]} onClose={() => {}} />)

    const openButton = screen.getByRole('button', { name: /B1/i })
    await user.click(openButton)

    expect(await screen.findByText(/Erreur/)).toBeInTheDocument()

    mockedGetCompletedRunDetail.mockResolvedValueOnce(baseDetail)

    const retryButton = screen.getByRole('button', { name: /Réessayer/i })
    await user.click(retryButton)

    await waitFor(() => expect(mockedGetCompletedRunDetail).toHaveBeenCalledTimes(2))
    const productLines = await screen.findAllByText(/Produit Test/)
    expect(productLines.length).toBeGreaterThan(0)
  })
})
