// Modifications : ajout d'un mock des zones pour vérifier le compteur de comptages terminés.
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import { ThemeProvider } from '../../theme/ThemeProvider'
import { HomePage } from '../pages/home/HomePage'
import type { InventorySummary, Location } from '../types/inventory'

const fetchInventorySummaryMock = vi.hoisted(() =>
  vi.fn(async (): Promise<InventorySummary> => ({
      activeSessions: 3,
      openRuns: 1,
      conflicts: 2,
      lastActivityUtc: '2025-01-01T12:00:00Z',
      openRunDetails: [
        {
          runId: 'run-1',
          locationId: 'loc-1',
          locationCode: 'Z1',
          locationLabel: 'Zone 1',
          countType: 1,
          operatorDisplayName: 'Amélie',
          startedAtUtc: '2025-01-01T10:00:00Z',
        },
      ],
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
  vi.fn(async (): Promise<Location[]> => [
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
            operatorDisplayName: 'Amélie',
            startedAtUtc: new Date('2025-01-01T09:00:00Z'),
            completedAtUtc: new Date('2025-01-01T10:00:00Z'),
          },
          {
            countType: 2,
            status: 'not_started',
            runId: null,
            operatorDisplayName: null,
            startedAtUtc: null,
            completedAtUtc: null,
          },
        ],
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
            operatorDisplayName: 'Benoît',
            startedAtUtc: new Date('2025-01-01T11:00:00Z'),
            completedAtUtc: null,
          },
          {
            countType: 2,
            status: 'completed',
            runId: 'run-c',
            operatorDisplayName: 'Claire',
            startedAtUtc: new Date('2025-01-01T08:30:00Z'),
            completedAtUtc: new Date('2025-01-01T09:30:00Z'),
          },
        ],
      },
    ]),
)

vi.mock('../api/inventoryApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/inventoryApi')>()
  return {
    ...actual,
    fetchInventorySummary: fetchInventorySummaryMock,
    fetchLocations: fetchLocationsMock,
  }
})

describe('HomePage', () => {
  it("affiche les indicateurs et le bouton d'accès", async () => {
    render(
      <ThemeProvider>
        <MemoryRouter>
          <HomePage />
        </MemoryRouter>
      </ThemeProvider>,
    )

    await waitFor(() => {
      expect(screen.getByRole('heading', { name: /État de l/i })).toBeInTheDocument()
      expect(screen.getAllByText('Comptages en cours')[0]).toBeInTheDocument()
      expect(screen.getByText('1')).toBeInTheDocument()
      expect(screen.getByText('Conflits')).toBeInTheDocument()
      expect(screen.getByText('2')).toBeInTheDocument()
      expect(screen.getByText('Touchez pour voir le détail')).toBeInTheDocument()
      expect(screen.getByText('Touchez une zone pour voir le détail')).toBeInTheDocument()
      expect(screen.getByText('Comptages terminés : 2 / 4')).toBeInTheDocument()
    })

    expect(screen.getByRole('button', { name: 'Débuter un inventaire' })).toBeInTheDocument()
    expect(fetchLocationsMock).toHaveBeenCalled()
  })

  it('affiche les messages neutres quand il ne reste plus de conflit ni de comptage', async () => {
    fetchInventorySummaryMock.mockResolvedValueOnce({
      activeSessions: 0,
      openRuns: 0,
      conflicts: 0,
      lastActivityUtc: null,
      openRunDetails: [],
      conflictZones: [],
    })
    fetchLocationsMock.mockResolvedValueOnce([])

    render(
      <ThemeProvider>
        <MemoryRouter>
          <HomePage />
        </MemoryRouter>
      </ThemeProvider>,
    )

    await waitFor(() => {
      expect(screen.getByText('Aucun comptage en cours')).toBeInTheDocument()
      expect(screen.getByText('Aucun conflit')).toBeInTheDocument()
      expect(screen.getByText('Comptages terminés : 0 / 0')).toBeInTheDocument()
    })
  })
})
