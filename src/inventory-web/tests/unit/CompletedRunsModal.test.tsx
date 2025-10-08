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
  ownerDisplayName: 'Toto',
  ownerUserId: 'user-toto',
  startedAtUtc: '2025-12-17T09:00:00Z',
  completedAtUtc: '2025-12-17T10:00:00Z',
}

const baseDetail = {
  runId: 'run-1',
  locationId: 'loc-1',
  locationCode: 'B1',
  locationLabel: 'Zone B1',
  countType: 1,
  ownerDisplayName: 'Toto',
  ownerUserId: 'user-toto',
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
      Reflect.deleteProperty(URL as { createObjectURL?: unknown }, 'createObjectURL')
    }

    if (originalRevokeObjectURL) {
      Object.defineProperty(URL, 'revokeObjectURL', {
        configurable: true,
        writable: true,
        value: originalRevokeObjectURL,
      })
    } else {
      Reflect.deleteProperty(URL as { revokeObjectURL?: unknown }, 'revokeObjectURL')
    }
  })

  it('affiche le détail et permet l’export CSV', async () => {
    render(<CompletedRunsModal open completedRuns={[baseRun]} onClose={() => {}} />)

    const openButtons = screen.getAllByRole('button', { name: /B1/i })
    expect(openButtons.length).toBeGreaterThan(0)
    await user.click(openButtons[0])

    await waitFor(() => expect(mockedGetCompletedRunDetail).toHaveBeenCalledWith('run-1'))

    expect(await screen.findByText(/Détail du comptage/)).toBeInTheDocument()
    expect(screen.getByText(/Produit Test/)).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: 'SKU' })).toBeInTheDocument()
    expect(screen.getByText('SKU-1')).toBeInTheDocument()

    const exportButton = screen.getByRole('button', { name: /Exporter en CSV/i })
    await user.click(exportButton)

    expect(createObjectUrlMock).toHaveBeenCalledTimes(1)
    expect(revokeObjectUrlMock).toHaveBeenCalledTimes(1)
    expect(anchorClickMock).toHaveBeenCalledTimes(1)
  })

  it('permet un export CSV global de tous les comptages', async () => {
    mockedGetCompletedRunDetail.mockClear()

    mockedGetCompletedRunDetail.mockImplementation(async (runId: string) => ({
      ...baseDetail,
      runId,
      locationId: runId === 'run-1' ? 'loc-1' : 'loc-2',
      locationCode: runId === 'run-1' ? 'B1' : 'C2',
      locationLabel: runId === 'run-1' ? 'Zone B1' : 'Zone C2',
      countType: runId === 'run-1' ? 1 : 2,
      ownerDisplayName: runId === 'run-1' ? 'Toto' : 'Léa',
      completedAtUtc: runId === 'run-1' ? '2025-12-17T10:00:00Z' : '2025-12-18T11:15:00Z',
      items: [
        {
          productId: `prod-${runId}`,
          sku: runId === 'run-1' ? 'SKU-1' : 'SKU-2',
          name: runId === 'run-1' ? 'Produit Test' : 'Produit 2',
          ean: runId === 'run-1' ? '123' : '456',
          quantity: runId === 'run-1' ? 3 : 5,
        },
      ],
    }))

    render(
      <CompletedRunsModal
        open
        completedRuns={[
          baseRun,
          {
            ...baseRun,
            runId: 'run-2',
            locationId: 'loc-2',
            locationCode: 'C2',
            locationLabel: 'Zone C2',
            countType: 2,
            completedAtUtc: '2025-12-18T11:15:00Z',
            ownerDisplayName: 'Léa',
          },
        ]}
        onClose={() => {}}
      />,
    )

    const exportAllButtons = screen.getAllByTestId('export-all-button')
    expect(exportAllButtons.length).toBeGreaterThan(0)
    await user.click(exportAllButtons[0])

    await waitFor(() => expect(createObjectUrlMock).toHaveBeenCalled())
    expect(createObjectUrlMock).toHaveBeenCalled()
    expect(anchorClickMock).toHaveBeenCalled()
    expect(revokeObjectUrlMock).toHaveBeenCalled()
    expect(screen.queryByText(/Impossible de générer le CSV global/i)).not.toBeInTheDocument()
  })

  it('affiche l’erreur et relance le chargement', async () => {
    mockedGetCompletedRunDetail.mockRejectedValueOnce(new Error('Oups'))

    render(<CompletedRunsModal open completedRuns={[baseRun]} onClose={() => {}} />)

    const openButtons = screen.getAllByRole('button', { name: /B1/i })
    expect(openButtons.length).toBeGreaterThan(0)
    await user.click(openButtons[0])

    expect(await screen.findByText(/Erreur/)).toBeInTheDocument()

    mockedGetCompletedRunDetail.mockResolvedValueOnce(baseDetail)

    const retryButton = screen.getByRole('button', { name: /Réessayer/i })
    await user.click(retryButton)

    await waitFor(() => expect(mockedGetCompletedRunDetail).toHaveBeenCalledTimes(2))
    const productLines = await screen.findAllByText(/Produit Test/)
    expect(productLines.length).toBeGreaterThan(0)
  })
})
