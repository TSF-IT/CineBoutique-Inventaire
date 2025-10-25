import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import type { CompletedRunSummary } from '../../types/inventory'

import { CompletedRunsModal } from './CompletedRunsModal'

const buildRun = (overrides: Partial<CompletedRunSummary>): CompletedRunSummary => ({
  runId: 'run-default',
  locationId: 'loc-default',
  locationCode: 'Z0',
  locationLabel: 'Zone Zéro',
  countType: 1,
  ownerDisplayName: 'Camille',
  ownerUserId: 'user-1',
  startedAtUtc: '2024-01-01T08:00:00Z',
  completedAtUtc: '2024-01-01T09:00:00Z',
  ...overrides,
})

describe('CompletedRunsModal', () => {
  it('classe les comptages par libellé de zone et masque le code dans le titre', () => {
    const runs: CompletedRunSummary[] = [
      buildRun({
        runId: 'run-b',
        locationId: 'loc-b',
        locationCode: 'B1',
        locationLabel: 'Zone B',
        completedAtUtc: '2024-01-03T09:00:00Z',
        countType: 2,
      }),
      buildRun({
        runId: 'run-a',
        locationId: 'loc-a',
        locationCode: 'A1',
        locationLabel: 'Zone A',
        completedAtUtc: '2024-01-02T09:00:00Z',
        countType: 1,
      }),
      buildRun({
        runId: 'run-c',
        locationId: 'loc-c',
        locationCode: 'C1',
        locationLabel: 'Zone C',
        completedAtUtc: '2024-01-01T09:00:00Z',
        countType: 3,
      }),
    ]

    render(<CompletedRunsModal open completedRuns={runs} onClose={() => {}} />)

    const list = screen.getByRole('list')
    const items = within(list).getAllByRole('listitem')
    const headers = items.map((item) => item.querySelector('p')?.textContent?.trim())

    expect(headers).toEqual(['Zone A', 'Zone B', 'Zone C'])
    headers.forEach((header) => {
      expect(header).toBeDefined()
      expect(header).not.toContain('A1')
      expect(header).not.toContain('B1')
      expect(header).not.toContain('C1')
      expect(header).not.toContain('·')
    })
  })

  it('classe les comptages d’une même zone par numéro croissant', () => {
    const runs: CompletedRunSummary[] = [
      buildRun({
        runId: 'run-zone-a-2',
        locationId: 'loc-a',
        locationCode: 'A1',
        locationLabel: 'Zone A',
        countType: 2,
        completedAtUtc: '2024-01-01T09:00:00Z',
      }),
      buildRun({
        runId: 'run-zone-a-1',
        locationId: 'loc-a',
        locationCode: 'A1',
        locationLabel: 'Zone A',
        countType: 1,
        completedAtUtc: '2024-01-02T09:00:00Z',
      }),
      buildRun({
        runId: 'run-zone-a-3',
        locationId: 'loc-a',
        locationCode: 'A1',
        locationLabel: 'Zone A',
        countType: 3,
        completedAtUtc: '2024-01-03T09:00:00Z',
      }),
    ]

    render(<CompletedRunsModal open completedRuns={runs} onClose={() => {}} />)

    const list = screen.getByRole('list')
    const items = within(list).getAllByRole('listitem')
    const subtitles = items.map((item) =>
      within(item).getByText(/Comptage n°/i).textContent?.trim(),
    )

    expect(subtitles).toEqual(['Comptage n°1', 'Comptage n°2', 'Comptage n°3'])
  })
})
