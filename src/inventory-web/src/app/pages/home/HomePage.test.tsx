import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { HomePage } from './HomePage'
import { ShopProvider } from '@/state/ShopContext'
import type { ConflictZoneDetail, InventorySummary, Location } from '../../types/inventory'
import { fetchInventorySummary, fetchLocations, getConflictZoneDetail } from '../../api/inventoryApi'
import { ThemeProvider } from '../../../theme/ThemeProvider'

const navigateMock = vi.fn()

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom')
  return {
    ...actual,
    useNavigate: () => navigateMock,
  }
})

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
    localStorage.clear()
    mockedFetchSummary.mockReset()
    mockedFetchLocations.mockReset()
    mockedGetDetail.mockReset()
    navigateMock.mockReset()
  })

  afterEach(() => {
    vi.clearAllMocks()
  })

  it('affiche les zones en conflit et ouvre la modale de détail', async () => {
    const summary: InventorySummary = {
      activeSessions: 0,
      openRuns: 0,
      completedRuns: 0,
      conflicts: 1,
      lastActivityUtc: null,
      openRunDetails: [],
      completedRunDetails: [],
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
        <ShopProvider>
          <MemoryRouter>
            <HomePage />
          </MemoryRouter>
        </ShopProvider>
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

  it('ouvre les modales des comptages en cours et terminés', async () => {
    const summary: InventorySummary = {
      activeSessions: 1,
      openRuns: 1,
      completedRuns: 1,
      conflicts: 0,
      lastActivityUtc: null,
      openRunDetails: [
        {
          runId: 'run-1',
          locationId: 'loc-1',
          locationCode: 'B1',
          locationLabel: 'Zone B1',
          countType: 1,
          ownerDisplayName: 'Alice',
          ownerUserId: 'user-alice',
          startedAtUtc: new Date('2024-01-01T10:00:00Z').toISOString(),
        },
      ],
      completedRunDetails: [
        {
          runId: 'run-2',
          locationId: 'loc-2',
          locationCode: 'S1',
          locationLabel: 'Zone S1',
          countType: 2,
          ownerDisplayName: 'Utilisateur Nice 1',
          ownerUserId: 'user-nice-1',
          startedAtUtc: new Date('2023-12-31T09:00:00Z').toISOString(),
          completedAtUtc: new Date('2023-12-31T10:00:00Z').toISOString(),
        },
      ],
      conflictZones: [],
    }

    const locations: Location[] = []

    mockedFetchSummary.mockResolvedValue(summary)
    mockedFetchLocations.mockResolvedValue(locations)

    render(
      <ThemeProvider>
        <ShopProvider>
          <MemoryRouter>
            <HomePage />
          </MemoryRouter>
        </ShopProvider>
      </ThemeProvider>,
    )

    await waitFor(() => {
      const buttons = screen.getAllByRole('button', { name: /Comptages en cours/i })
      expect(buttons.some((button) => !button.hasAttribute('disabled'))).toBe(true)
    })

    const openRunsCards = screen.getAllByRole('button', { name: /Comptages en cours/i })
    const openRunsCard = openRunsCards.find((button) => !button.hasAttribute('disabled')) ?? openRunsCards[0]
    expect(openRunsCard).toBeDefined()
    fireEvent.click(openRunsCard)

    const openRunsModalTitle = await screen.findByRole('heading', { level: 2, name: /Comptages en cours/i })
    expect(openRunsModalTitle).toBeInTheDocument()
    const openRunsModal = await screen.findByRole('dialog', { name: /Comptages en cours/i })
    expect(screen.getByText(/Opérateur : Alice/i)).toBeInTheDocument()

    const closeOpenRunsButton = within(openRunsModal).getByRole('button', { name: /Fermer/i })
    fireEvent.click(closeOpenRunsButton)

    await waitFor(() => {
      expect(screen.queryByRole('heading', { level: 2, name: /Comptages en cours/i })).not.toBeInTheDocument()
    })

    await waitFor(() => {
      const buttons = screen.getAllByRole('button', { name: /Comptages terminés/i })
      expect(buttons.some((button) => !button.hasAttribute('disabled'))).toBe(true)
    })

    const completedRunsCards = screen.getAllByRole('button', { name: /Comptages terminés/i })
    const completedRunsCard = completedRunsCards.find((button) => !button.hasAttribute('disabled')) ?? completedRunsCards[0]
    expect(completedRunsCard).toBeDefined()
    fireEvent.click(completedRunsCard)

    const completedRunsModalTitle = await screen.findByRole('heading', { level: 2, name: /Comptages terminés/i })
    expect(completedRunsModalTitle).toBeInTheDocument()
    const completedRunsModal = await screen.findByRole('dialog', { name: /Comptages terminés/i })
    expect(within(completedRunsModal).getByText(/Comptages terminés \(20 plus récents\)/i)).toBeInTheDocument()
    expect(within(completedRunsModal).getByText(/Opérateur : Utilisateur Nice 1/i)).toBeInTheDocument()
  })

  it("redirige vers l'assistant d'inventaire au clic sur le CTA principal", async () => {
    const summary: InventorySummary = {
      activeSessions: 0,
      openRuns: 0,
      completedRuns: 0,
      conflicts: 0,
      lastActivityUtc: null,
      openRunDetails: [],
      completedRunDetails: [],
      conflictZones: [],
    }

    const locations: Location[] = []

    mockedFetchSummary.mockResolvedValue(summary)
    mockedFetchLocations.mockResolvedValue(locations)

    render(
      <ThemeProvider>
        <ShopProvider>
          <MemoryRouter>
            <HomePage />
          </MemoryRouter>
        </ShopProvider>
      </ThemeProvider>,
    )

    const buttons = await screen.findAllByRole('button', { name: /Débuter un inventaire/i })
    expect(buttons.length).toBeGreaterThan(0)
    fireEvent.click(buttons[0])

    expect(navigateMock).toHaveBeenCalledWith('/inventory/start')
  })
})
