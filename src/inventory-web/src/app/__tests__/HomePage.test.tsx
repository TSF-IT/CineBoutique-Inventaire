import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import { ThemeProvider } from '../../theme/ThemeProvider'
import { HomePage } from '../pages/home/HomePage'
import type { InventorySummary } from '../types/inventory'

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
      conflictDetails: [
        {
          conflictId: 'conf-1',
          countLineId: 'line-1',
          countingRunId: 'run-1',
          locationId: 'loc-1',
          locationCode: 'Z1',
          locationLabel: 'Zone 1',
          countType: 1,
          operatorDisplayName: 'Amélie',
          createdAtUtc: '2025-01-01T11:00:00Z',
        },
      ],
    })),
)

vi.mock('../api/inventoryApi', () => ({
  fetchInventorySummary: fetchInventorySummaryMock,
}))

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
      expect(screen.getAllByText('Touchez pour voir le détail')).toHaveLength(2)
    })

    expect(screen.getByRole('button', { name: 'Débuter un inventaire' })).toBeInTheDocument()
  })

  it('affiche les messages neutres quand il ne reste plus de conflit ni de comptage', async () => {
    fetchInventorySummaryMock.mockResolvedValueOnce({
      activeSessions: 0,
      openRuns: 0,
      conflicts: 0,
      lastActivityUtc: null,
      openRunDetails: [],
      conflictDetails: [],
    })

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
    })
  })
})
