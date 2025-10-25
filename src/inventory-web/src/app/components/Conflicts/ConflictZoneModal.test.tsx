import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { Mock } from 'vitest'

import { getConflictZoneDetail } from '../../api/inventoryApi'
import type { ConflictZoneSummary } from '../../types/inventory'

import { ConflictZoneModal } from './ConflictZoneModal'


vi.mock('../../api/inventoryApi', () => ({
  getConflictZoneDetail: vi.fn(),
}))

const getConflictZoneDetailMock = getConflictZoneDetail as Mock

const baseZone: ConflictZoneSummary = {
  locationId: 'loc-1',
  locationCode: 'Z1',
  locationLabel: 'Zone 1',
  conflictLines: 1,
}

describe('ConflictZoneModal', () => {
  beforeEach(() => {
    getConflictZoneDetailMock.mockReset()
  })

  afterEach(() => {
    cleanup()
  })

  it('affiche toutes les colonnes dynamiques quand les runs sont fournis', async () => {
    getConflictZoneDetailMock.mockResolvedValue({
      locationId: baseZone.locationId,
      locationCode: baseZone.locationCode,
      locationLabel: baseZone.locationLabel,
      runs: [
        { runId: 'run-1', countType: 1, completedAtUtc: '2024-01-01T10:00:00Z', ownerDisplayName: 'Alice' },
        { runId: 'run-2', countType: 2, completedAtUtc: '2024-01-01T11:00:00Z', ownerDisplayName: 'Bastien' },
        { runId: 'run-3', countType: 3, completedAtUtc: '2024-01-01T12:00:00Z', ownerDisplayName: 'Chloé' },
      ],
      items: [
        {
          productId: 'prod-1',
          sku: 'SKU-111',
          ean: '111',
          qtyC1: 5,
          qtyC2: 8,
          delta: -3,
          allCounts: [
            { runId: 'run-1', countType: 1, quantity: 5 },
            { runId: 'run-2', countType: 2, quantity: 8 },
            { runId: 'run-3', countType: 3, quantity: 6 },
          ],
        },
      ],
    })

    render(<ConflictZoneModal open zone={baseZone} onClose={() => {}} />)

    await waitFor(() => expect(getConflictZoneDetailMock).toHaveBeenCalled())

    expect(await screen.findByText('Comptage 1')).toBeInTheDocument()
    expect(await screen.findByText('Comptage 2')).toBeInTheDocument()
    expect(screen.getByText('Comptage 3')).toBeInTheDocument()

    const skuElement = await screen.findByText('SKU-111')
    const card = skuElement.closest('article.conflict-card') as HTMLElement | null
    expect(card).not.toBeNull()
    if (card) {
      const scoped = within(card)
      expect(scoped.getByText('Alice')).toBeInTheDocument()
      expect(scoped.getByText('Chloé')).toBeInTheDocument()
      expect(scoped.getByText('6')).toBeInTheDocument()
      expect(scoped.getByText('-3')).toBeInTheDocument()
      const skuLabel = scoped.getByText('SKU')
      expect(skuLabel.parentElement?.textContent).toContain('SKU-111')
    }
  })

  it('retombe sur le rendu legacy quand les runs ne sont pas fournis', async () => {
    getConflictZoneDetailMock.mockResolvedValue({
      locationId: baseZone.locationId,
      locationCode: baseZone.locationCode,
      locationLabel: baseZone.locationLabel,
      items: [
        {
          productId: 'prod-2',
          ean: '222',
          qtyC1: 4,
          qtyC2: 7,
          delta: -3,
        },
      ],
    })

    render(<ConflictZoneModal open zone={baseZone} onClose={() => {}} />)

    await waitFor(() => expect(getConflictZoneDetailMock).toHaveBeenCalled())

    expect(await screen.findByText('Comptage 1')).toBeInTheDocument()
    expect(await screen.findByText('Comptage 2')).toBeInTheDocument()
    const eanElement = await screen.findByText('222')
    const card = eanElement.closest('article.conflict-card') as HTMLElement | null
    expect(card).not.toBeNull()
    if (card) {
      const scoped = within(card)
      expect(scoped.queryByText('Comptage 3')).not.toBeInTheDocument()
      const skuLabel = scoped.getByText('SKU')
      expect(skuLabel.parentElement?.textContent).toContain('—')
    }
  })

  it('propose de lancer un nouveau comptage lorsque le callback est fourni', async () => {
    getConflictZoneDetailMock.mockResolvedValue({
      locationId: baseZone.locationId,
      locationCode: baseZone.locationCode,
      locationLabel: baseZone.locationLabel,
      runs: [
        { runId: 'run-1', countType: 1, completedAtUtc: '2024-01-01T08:30:00Z', ownerDisplayName: 'Alice' },
        { runId: 'run-2', countType: 2, completedAtUtc: '2024-01-01T09:30:00Z', ownerDisplayName: 'Bob' },
      ],
      items: [],
    })

    const onStart = vi.fn()

    render(<ConflictZoneModal open zone={baseZone} onClose={() => {}} onStartExtraCount={onStart} />)

    const launchButton = await screen.findByRole('button', { name: /Lancer le 3.? comptage/i })
    fireEvent.click(launchButton)

    expect(onStart).toHaveBeenCalledWith(baseZone, 3)
  })
})
