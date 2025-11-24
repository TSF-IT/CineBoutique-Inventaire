import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { CompletedRunDetail, CompletedRunSummary } from '../../types/inventory'

import { CompletedRunsModal, mergeRunDetailWithSummary } from './CompletedRunsModal'

const buildRun = (overrides: Partial<CompletedRunSummary>): CompletedRunSummary => ({
  runId: 'run-default',
  locationId: 'loc-default',
  locationCode: 'Z0',
  locationLabel: 'Zone Z\u00e9ro',
  countType: 1,
  ownerDisplayName: 'Camille',
  ownerUserId: 'user-1',
  startedAtUtc: '2024-01-01T08:00:00Z',
  completedAtUtc: '2024-01-01T09:00:00Z',
  ...overrides,
})

const getLatestModalContainer = () => {
  const modals = document.querySelectorAll<HTMLElement>('[data-modal-container]')
  return modals.length > 0 ? modals[modals.length - 1] : null
}

describe('CompletedRunsModal', () => {
  it('classe les comptages par libell\u00e9 de zone et masque le code dans le titre', () => {
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

    const lists = screen.getAllByRole('list')
    const list = lists[0]
    const items = within(list).getAllByRole('listitem')
    const headers = items.map((item) => item.querySelector('p')?.textContent?.trim())

    expect(headers).toEqual(['Zone A', 'Zone B', 'Zone C'])
    headers.forEach((header) => {
      expect(header).toBeDefined()
      expect(header).not.toContain('A1')
      expect(header).not.toContain('B1')
      expect(header).not.toContain('C1')
      expect(header).not.toContain('\ufffd')
    })
  })

  it("classe les comptages d'une m\u00eame zone par num\u00e9ro croissant", () => {
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

    const modal = getLatestModalContainer()
    expect(modal).not.toBeNull()
    const lists = within(modal as HTMLElement).getAllByRole('list')
    const items = within(lists[0]).getAllByRole('listitem')
    const subtitles = items.map((item) =>
      within(item).getByText(/Comptage n\u00b0/i).textContent?.trim() ?? '',
    )
    const countLabels = subtitles.map((text) => {
      const match = text.match(/Comptage n\u00b0\d/)
      return match ? match[0] : text
    })

    expect(countLabels).toEqual(['Comptage n\u00b01', 'Comptage n\u00b02', 'Comptage n\u00b03'])
  })

  it("conserve l'op\u00e9rateur du r\u00e9cap lorsqu'il manque dans le d\u00e9tail", () => {
    const summary = buildRun({ runId: 'run-a', ownerDisplayName: 'Alice', ownerUserId: 'user-alice' })
    const detail: CompletedRunDetail = {
      runId: 'run-a',
      locationId: 'loc-a',
      locationCode: '',
      locationLabel: '',
      countType: 1,
      ownerDisplayName: null,
      ownerUserId: null,
      startedAtUtc: '2024-01-01T08:00:00Z',
      completedAtUtc: '2024-01-01T09:00:00Z',
      items: [
        {
          productId: 'product-1',
          sku: 'SKU-1',
          name: 'Produit 1',
          ean: '1111111111111',
          quantity: 2,
        },
      ],
    }

    const merged = mergeRunDetailWithSummary(detail, summary)

    expect(merged.ownerDisplayName).toBe('Alice')
    expect(merged.ownerUserId).toBe('user-alice')
    expect(merged.locationCode).toBe('Z0')
    expect(merged.locationLabel).toBe('Zone Z\u00e9ro')
  })
})
