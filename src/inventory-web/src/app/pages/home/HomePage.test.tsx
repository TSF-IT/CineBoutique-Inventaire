import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { HomePage } from './HomePage'
import type { ConflictZoneDetail, InventorySummary, Location } from '../../types/inventory'
import { fetchInventorySummary, fetchLocations, getConflictZoneDetail } from '../../api/inventoryApi'
import { ThemeProvider } from '../../../theme/ThemeProvider'

vi.mock('../../api/inventoryApi', () => ({
  fetchInventorySummary: vi.fn(),
  fetchLocations: vi.fn(),
  getConflictZoneDetail: vi.fn(),
}))

const {
  fetchInventorySummary: mockedFetchSummary,
  fetchLocations: mockedFetchLocations,
  getConflictZoneDetail: mockedGetDetail,
} = vi.mocked({ fetchInventorySummary, fetchLocations, getConflictZoneDetail })

describe('HomePage', () => {
  beforeEach(() => {
    mockedFetchSummary.mockReset()
    mockedFetchLocations.mockReset()
    mockedGetDetail.mockReset()
  })

  afterEach(() => {
    vi.clearAllMocks()
  })

  it('affiche les zones en conflit et ouvre la modale de détail', async () => {
    const summary: InventorySummary = {
      activeSessions: 0,
      openRuns: 0,
      conflicts: 1,
      lastActivityUtc: null,
      openRunDetails: [],
      conflictZones: [
        {
          locationId: 'loc-1',
          locationCode: 'B1',
          locationLabel: 'Zone B1',
          conflictLines: 2,
        },
      ],
    }

    const locations: Location[] = []
    const detail: ConflictZoneDetail = {
      locationId: 'loc-1',
      locationCode: 'B1',
      locationLabel: 'Zone B1',
      items: [
        { productId: 'p1', ean: '111', qtyC1: 5, qtyC2: 8, delta: -3 },
        { productId: 'p2', ean: '222', qtyC1: 3, qtyC2: 1, delta: 2 },
      ],
    }

    mockedFetchSummary.mockResolvedValue(summary)
    mockedFetchLocations.mockResolvedValue(locations)
    mockedGetDetail.mockResolvedValue(detail)

    render(
      <ThemeProvider>
        <MemoryRouter>
          <HomePage />
        </MemoryRouter>
      </ThemeProvider>,
    )

    await waitFor(() => {
      expect(mockedFetchSummary).toHaveBeenCalled()
    })

    expect(await screen.findByText('Conflits')).toBeInTheDocument()
    expect(screen.getByText('1')).toBeInTheDocument()
    const zoneButton = await screen.findByRole('button', { name: /B1 · Zone B1/i })
    fireEvent.click(zoneButton)

    const dialog = await screen.findByRole('dialog', { name: /B1 · Zone B1/i })
    expect(dialog).toBeInTheDocument()
    expect(await screen.findByText(/EAN 111/i)).toBeInTheDocument()
    expect(screen.getAllByText('Comptage 1').length).toBeGreaterThan(0)
    expect(screen.getAllByText('Comptage 2').length).toBeGreaterThan(0)
  })
})
