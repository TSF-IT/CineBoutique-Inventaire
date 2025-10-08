import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import type { ReactNode } from 'react'
import { useEffect } from 'react'
import { HomePage } from './HomePage'
import { ShopProvider } from '@/state/ShopContext'
import type { ConflictZoneDetail, InventorySummary, Location } from '../../types/inventory'
import type { HttpError } from '@/lib/api/http'
import {
  fetchInventorySummary,
  fetchLocationSummaries,
  fetchLocations,
  getConflictZoneDetail,
} from '../../api/inventoryApi'
import { ThemeProvider } from '../../../theme/ThemeProvider'
import { InventoryProvider, useInventory } from '../../contexts/InventoryContext'
import type { ShopUser } from '@/types/user'

const navigateMock = vi.fn()
const testShop = { id: 'shop-1', name: 'Boutique de test' }

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom')
  return {
    ...actual,
    useNavigate: () => navigateMock,
  }
})

vi.mock('../../api/inventoryApi', () => ({
  fetchInventorySummary: vi.fn(),
  fetchLocationSummaries: vi.fn(),
  fetchLocations: vi.fn(),
  getConflictZoneDetail: vi.fn(),
}))

const {
  fetchInventorySummary: mockedFetchSummary,
  fetchLocationSummaries: mockedFetchLocationSummaries,
  fetchLocations: mockedFetchLocations,
  getConflictZoneDetail: mockedGetDetail,
} = vi.mocked({
  fetchInventorySummary,
  fetchLocationSummaries,
  fetchLocations,
  getConflictZoneDetail,
})

const defaultUser: ShopUser = {
  id: 'user-alice',
  shopId: testShop.id,
  login: 'alice',
  displayName: 'Alice',
  isAdmin: false,
  disabled: false,
}

const InventoryUserInitializer = ({ user }: { user: ShopUser | null }) => {
  const { setSelectedUser } = useInventory()
  useEffect(() => {
    if (user) {
      setSelectedUser(user)
    }
  }, [setSelectedUser, user])
  return null
}

const withProviders = (ui: ReactNode, user: ShopUser | null = defaultUser) => (
  <ThemeProvider>
    <ShopProvider>
      <InventoryProvider>
        <InventoryUserInitializer user={user} />
        <MemoryRouter>{ui}</MemoryRouter>
      </InventoryProvider>
    </ShopProvider>
  </ThemeProvider>
)

const renderHomePage = (options?: { user?: ShopUser | null }) => {
  const providedUser = options && 'user' in options ? options.user : undefined
  const resolvedUser = providedUser === undefined ? defaultUser : providedUser
  return render(withProviders(<HomePage />, resolvedUser))
}

describe('HomePage', () => {
  beforeEach(() => {
    localStorage.clear()
    localStorage.setItem('cb.shop', JSON.stringify(testShop))
    mockedFetchSummary.mockReset()
    mockedFetchLocationSummaries.mockReset()
    mockedFetchLocations.mockReset()
    mockedGetDetail.mockReset()
    navigateMock.mockReset()
  })

  afterEach(() => {
    vi.clearAllMocks()
  })

  it('redirige vers la sélection de boutique si aucune boutique n’est mémorisée', async () => {
    localStorage.removeItem('cb.shop')
    renderHomePage({ user: null })

    await waitFor(() => {
      expect(navigateMock).toHaveBeenCalledWith('/select-shop', { replace: true })
    })

    expect(mockedFetchSummary).not.toHaveBeenCalled()
    expect(mockedFetchLocations).not.toHaveBeenCalled()
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
        { sku: 'SKU-001', productId: 'p1', ean: '111', qtyC1: 5, qtyC2: 8, delta: -3 },
        { sku: 'SKU-002', productId: 'p2', ean: '222', qtyC1: 3, qtyC2: 1, delta: 2 },
      ],
    }

    mockedFetchSummary.mockResolvedValue(summary)
    mockedFetchLocationSummaries.mockResolvedValue([])
    mockedFetchLocations.mockResolvedValue(locations)
    mockedGetDetail.mockResolvedValue(detail)

    renderHomePage()

    await waitFor(() => {
      expect(mockedFetchSummary).toHaveBeenCalled()
    })

    expect(await screen.findByText('Conflits')).toBeInTheDocument()
    expect(screen.getByText('1')).toBeInTheDocument()
    const zoneButton = await screen.findByRole('button', { name: /B1 · Zone B1/i })
    fireEvent.click(zoneButton)

    const dialog = await screen.findByRole('dialog', { name: /B1 · Zone B1/i })
    expect(dialog).toBeInTheDocument()

    const eanLine = await within(dialog).findByText((content, element) => {
      if (!element) return false
      return element.classList.contains('conflict-card__ean') && content.includes('111')
    })
    expect(eanLine).toBeInTheDocument()
    expect(within(dialog).getAllByText('Comptage 1').length).toBeGreaterThan(0)
    expect(within(dialog).getAllByText('Comptage 2').length).toBeGreaterThan(0)
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

    renderHomePage()

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
    expect(within(completedRunsModal).getByText(/Consultez les comptages finalisés\./i)).toBeInTheDocument()
    expect(within(completedRunsModal).getByText(/Opérateur : Utilisateur Nice 1/i)).toBeInTheDocument()
  })

  it('ne charge les emplacements qu’une seule fois', async () => {
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

    mockedFetchSummary.mockResolvedValue(summary)
    mockedFetchLocations.mockResolvedValue([])

    renderHomePage()

    await waitFor(() => {
      expect(mockedFetchSummary).toHaveBeenCalledTimes(1)
      expect(mockedFetchLocations).toHaveBeenCalledTimes(1)
    })
  })

  it("permet de reprendre un comptage appartenant à l'utilisateur courant", async () => {
    const startedAt = new Date('2024-01-01T10:00:00Z')
    const summary: InventorySummary = {
      activeSessions: 1,
      openRuns: 1,
      completedRuns: 0,
      conflicts: 0,
      lastActivityUtc: startedAt.toISOString(),
      openRunDetails: [
        {
          runId: 'run-1',
          locationId: 'loc-1',
          locationCode: 'B1',
          locationLabel: 'Zone B1',
          countType: 1,
          ownerDisplayName: 'Alice',
          ownerUserId: defaultUser.id,
          startedAtUtc: startedAt.toISOString(),
        },
      ],
      completedRunDetails: [],
      conflictZones: [],
    }

    const locations: Location[] = [
      {
        id: 'loc-1',
        code: 'B1',
        label: 'Zone B1',
        isBusy: true,
        busyBy: 'Alice',
        activeRunId: 'run-1',
        activeCountType: 1,
        activeStartedAtUtc: startedAt,
        countStatuses: [
          {
            countType: 1,
            status: 'in_progress',
            runId: 'run-1',
            ownerDisplayName: 'Alice',
            ownerUserId: defaultUser.id,
            startedAtUtc: startedAt,
            completedAtUtc: null,
          },
        ],
      },
    ]

    mockedFetchSummary.mockResolvedValue(summary)
    mockedFetchLocations.mockResolvedValue(locations)
    mockedFetchLocationSummaries.mockResolvedValue([])

    renderHomePage()

    await waitFor(() => expect(mockedFetchSummary).toHaveBeenCalled())

    const inProgressMessages = await screen.findAllByText(/Vous avez un comptage en cours/i)
    expect(inProgressMessages.length).toBeGreaterThan(0)

    const openRunsCards = screen.getAllByRole('button', { name: /Comptages en cours/i })
    const openRunsCard = openRunsCards.find((button) => !button.hasAttribute('disabled')) ?? openRunsCards[0]
    fireEvent.click(openRunsCard)

    const resumeButton = await screen.findByRole('button', { name: /Reprendre/i })
    fireEvent.click(resumeButton)

    await waitFor(() => {
      expect(navigateMock).toHaveBeenCalledWith('/inventory/session')
    })

    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: /Comptages en cours/i })).not.toBeInTheDocument()
    })
  })

  it('permet de lancer un nouveau comptage depuis un conflit', async () => {
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

    const locations: Location[] = [
      {
        id: 'loc-1',
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
            status: 'completed',
            runId: 'run-1',
            ownerDisplayName: 'Alice',
            ownerUserId: defaultUser.id,
            startedAtUtc: new Date('2024-01-01T08:00:00Z'),
            completedAtUtc: new Date('2024-01-01T08:30:00Z'),
          },
          {
            countType: 2,
            status: 'completed',
            runId: 'run-2',
            ownerDisplayName: 'Bob',
            ownerUserId: 'user-bob',
            startedAtUtc: new Date('2024-01-01T09:00:00Z'),
            completedAtUtc: new Date('2024-01-01T09:30:00Z'),
          },
        ],
      },
    ]

    const detail: ConflictZoneDetail = {
      locationId: 'loc-1',
      locationCode: 'B1',
      locationLabel: 'Zone B1',
      runs: [
        { runId: 'run-1', countType: 1, completedAtUtc: '2024-01-01T08:30:00Z', ownerDisplayName: 'Alice' },
        { runId: 'run-2', countType: 2, completedAtUtc: '2024-01-01T09:30:00Z', ownerDisplayName: 'Bob' },
      ],
      items: [],
    }

    mockedFetchSummary.mockResolvedValue(summary)
    mockedFetchLocations.mockResolvedValue(locations)
    mockedFetchLocationSummaries.mockResolvedValue([])
    mockedGetDetail.mockResolvedValue(detail)

    renderHomePage()

    const conflictButton = await screen.findByRole('button', { name: /B1 · Zone B1/i })
    fireEvent.click(conflictButton)

    const launchButton = await screen.findByRole('button', { name: /Lancer le 3.? comptage/i })
    fireEvent.click(launchButton)

    await waitFor(() => {
      expect(navigateMock).toHaveBeenCalledWith('/inventory/session')
    })

    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: /Zone B1/i })).not.toBeInTheDocument()
    })
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

    renderHomePage()

    const buttons = await screen.findAllByRole('button', { name: /Débuter un comptage/i })
    fireEvent.click(buttons[0])

    expect(navigateMock).toHaveBeenCalledWith('/inventory/location')
  })

  it('ignore le 404 produit sur la Home', async () => {
    const productNotFound = Object.assign(new Error('Produit introuvable'), {
      status: 404,
      url: 'http://localhost:5173/api/products/0000000000000',
    }) as HttpError

    mockedFetchSummary.mockRejectedValue(productNotFound)
    mockedFetchLocations.mockResolvedValue([])

    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {})

    try {
      renderHomePage()

      await waitFor(() => expect(mockedFetchSummary).toHaveBeenCalled())

      await waitFor(() => {
        expect(warnSpy).toHaveBeenCalledWith('[home] produit introuvable ignoré', productNotFound)
      })

      expect(errorSpy).not.toHaveBeenCalledWith('[home] http error', expect.anything())
      expect(screen.queryByRole('alert')).not.toBeInTheDocument()
      const placeholders = await screen.findAllByText('Les indicateurs ne sont pas disponibles pour le moment.')
      expect(placeholders.length).toBeGreaterThan(0)
    } finally {
      warnSpy.mockRestore()
      errorSpy.mockRestore()
    }
  })
})
