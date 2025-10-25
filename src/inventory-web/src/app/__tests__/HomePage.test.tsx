// Modifications : ajout d'un mock des zones pour vérifier le compteur de comptages terminés.
import { render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { ReactNode } from 'react'
import { useEffect } from 'react'
import { ThemeProvider } from '../../theme/ThemeProvider'
import { HomePage } from '../pages/home/HomePage'
import { ShopProvider } from '@/state/ShopContext'
import type { InventorySummary, Location } from '../types/inventory'
import type { LocationSummary } from '@/types/summary'
import { InventoryProvider, useInventory } from '../contexts/InventoryContext'
import type { ShopUser } from '@/types/user'

const fetchInventorySummaryMock = vi.hoisted(() =>
  vi.fn(async (): Promise<InventorySummary> => ({
      activeSessions: 3,
      openRuns: 1,
      completedRuns: 0,
      conflicts: 2,
      lastActivityUtc: '2025-01-01T12:00:00Z',
      openRunDetails: [
        {
          runId: 'run-1',
          locationId: 'loc-1',
          locationCode: 'Z1',
          locationLabel: 'Zone 1',
          countType: 1,
          ownerDisplayName: 'Utilisateur Paris',
          ownerUserId: 'user-paris',
          startedAtUtc: '2025-01-01T10:00:00Z',
        },
      ],
      completedRunDetails: [],
      conflictZones: [
        {
          locationId: 'loc-1',
          locationCode: 'Z1',
          locationLabel: 'Zone 1',
          conflictLines: 2,
        },
        {
          locationId: 'loc-2',
          locationCode: 'Z2',
          locationLabel: 'Zone 2',
          conflictLines: 1,
        },
      ],
    })),
)

const fetchLocationsMock = vi.hoisted(() =>
  vi.fn(async (shopId: string, _options?: { includeDisabled?: boolean }): Promise<Location[]> => {
    expect(shopId).toBeTruthy()
    return [
      {
        id: 'loc-1',
        code: 'Z1',
        label: 'Zone 1',
        isBusy: false,
        busyBy: null,
        activeRunId: null,
        activeCountType: null,
        activeStartedAtUtc: null,
        countStatuses: [
          {
            countType: 1,
            status: 'completed',
            runId: 'run-a',
            ownerDisplayName: 'Utilisateur Paris',
            ownerUserId: 'user-paris',
            startedAtUtc: new Date('2025-01-01T09:00:00Z'),
            completedAtUtc: new Date('2025-01-01T10:00:00Z'),
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
        disabled: false,
      },
      {
        id: 'loc-2',
        code: 'Z2',
        label: 'Zone 2',
        isBusy: false,
        busyBy: null,
        activeRunId: null,
        activeCountType: null,
        activeStartedAtUtc: null,
        countStatuses: [
          {
            countType: 1,
            status: 'in_progress',
            runId: 'run-b',
            ownerDisplayName: 'Benoît',
            ownerUserId: 'user-benoit',
            startedAtUtc: new Date('2025-01-01T11:00:00Z'),
            completedAtUtc: null,
          },
          {
            countType: 2,
            status: 'completed',
            runId: 'run-c',
            ownerDisplayName: 'Claire',
            ownerUserId: 'user-claire',
            startedAtUtc: new Date('2025-01-01T08:30:00Z'),
            completedAtUtc: new Date('2025-01-01T09:30:00Z'),
          },
        ],
        disabled: false,
      },
    ]
  }),
)

const fetchLocationSummariesMock = vi.hoisted(() =>
  vi.fn(async (shopId: string): Promise<LocationSummary[]> => {
    expect(shopId).toBeTruthy()
    return [
      {
        locationId: '00000000-0000-4000-8000-000000000010',
        locationName: 'Zone 1',
        busyBy: null,
        activeRunId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        activeCountType: 1,
        activeStartedAtUtc: new Date('2025-01-01T09:00:00Z'),
        countStatuses: [
          {
            runId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
            startedAtUtc: new Date('2025-01-01T09:00:00Z'),
            completedAtUtc: null,
          },
          {
            runId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
            startedAtUtc: new Date('2025-01-01T08:00:00Z'),
            completedAtUtc: new Date('2025-01-01T08:45:00Z'),
          },
        ],
      },
    ]
  }),
)

const defaultUser: ShopUser = {
  id: 'user-paris',
  shopId: 'shop-123',
  login: 'paris.user',
  displayName: 'Utilisateur Paris',
  isAdmin: false,
  disabled: false,
}

const InventoryUserInitializer = ({ user }: { user: ShopUser }) => {
  const { setSelectedUser } = useInventory()

  useEffect(() => {
    setSelectedUser(user)
  }, [setSelectedUser, user])

  return null
}

const withProviders = (ui: ReactNode) => (
  <ThemeProvider>
    <ShopProvider>
      <InventoryProvider>
        <InventoryUserInitializer user={defaultUser} />
        <MemoryRouter>{ui}</MemoryRouter>
      </InventoryProvider>
    </ShopProvider>
  </ThemeProvider>
)

vi.mock('../api/inventoryApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/inventoryApi')>()
  return {
    ...actual,
    fetchInventorySummary: fetchInventorySummaryMock,
    fetchLocationSummaries: fetchLocationSummariesMock,
    fetchLocations: fetchLocationsMock,
  }
})

describe('HomePage', () => {
  const testShop = { id: 'shop-123', name: 'Boutique test', kind: 'boutique' } as const

  beforeEach(() => {
    localStorage.clear()
    localStorage.setItem('cb.shop', JSON.stringify(testShop))
  })

  it("affiche les indicateurs et le bouton d'accès", async () => {
    render(withProviders(<HomePage />))

    await waitFor(() => {
      expect(screen.getByRole('heading', { name: /État de l/i })).toBeInTheDocument()

      const runningButton = screen.getByRole('button', { name: /Comptages en cours/i })
      expect(within(runningButton).getByText('1')).toBeInTheDocument()
      expect(within(runningButton).getByText(/Touchez pour voir le détail/i)).toBeInTheDocument()

      const conflictsCard = screen.getByText('Conflits').closest('div')
      expect(conflictsCard).not.toBeNull()
      if (conflictsCard) {
        expect(within(conflictsCard).getByText('2')).toBeInTheDocument()
        expect(within(conflictsCard).getByText(/Touchez une zone pour voir le détail/i)).toBeInTheDocument()
      }

      expect(screen.getByText('2 comptages terminés sur 4')).toBeInTheDocument()
      expect(screen.getByText('0 zones terminées sur 2')).toBeInTheDocument()
    })

    expect(screen.getByRole('button', { name: 'Débuter un comptage' })).toBeInTheDocument()
    expect(fetchLocationsMock).toHaveBeenCalled()
    const [shopIdArg, optionsArg] = fetchLocationsMock.mock.calls[0] ?? []
    expect(shopIdArg).toBe(testShop.id)
    expect(optionsArg).toEqual({ includeDisabled: true })
  })

  it('affiche les messages neutres quand il ne reste plus de conflit ni de comptage', async () => {
    fetchInventorySummaryMock.mockResolvedValueOnce({
      activeSessions: 0,
      openRuns: 0,
      completedRuns: 0,
      conflicts: 0,
      lastActivityUtc: null,
      openRunDetails: [],
      completedRunDetails: [],
      conflictZones: [],
    })
    fetchLocationsMock.mockResolvedValueOnce([])
    fetchLocationSummariesMock.mockResolvedValueOnce([])

    render(withProviders(<HomePage />))

    expect(await screen.findByText('Aucun comptage en cours')).toBeInTheDocument()
    expect(await screen.findByText('Aucun conflit')).toBeInTheDocument()
    expect(await screen.findByText('Aucun comptage terminé')).toBeInTheDocument()
    expect(await screen.findByText('Aucune zone terminée')).toBeInTheDocument()
    expect(await screen.findByText('Aucun comptage en cours pour cette boutique.')).toBeInTheDocument()
  })
})
