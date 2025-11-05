import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, beforeEach, afterEach, vi, expect } from 'vitest'

import { getConflictZoneDetail, getCompletedRunDetail } from '../api/inventoryApi'
import { ConflictZoneModal } from '../components/Conflicts/ConflictZoneModal'
import { MobileActionBar } from '../components/MobileActionBar'
import { PageShell } from '../components/PageShell'
import { CompletedRunsModal } from '../components/Runs/CompletedRunsModal'
import type { ConflictZoneDetail, ConflictZoneSummary, CompletedRunDetail, CompletedRunSummary } from '../types/inventory'

vi.mock('../api/inventoryApi', () => ({
  getConflictZoneDetail: vi.fn(),
  getCompletedRunDetail: vi.fn(),
}))

const createMatchMedia = (width: number, height: number) =>
  (query: string): MediaQueryList => {
    const matches = (() => {
      if (query.includes('orientation: portrait')) {
        return height >= width
      }
      if (query.includes('orientation: landscape')) {
        return width > height
      }
      const maxWidth = query.match(/max-width:\s*(\d+)px/)
      if (maxWidth) {
        return width <= Number(maxWidth[1])
      }
      const minWidth = query.match(/min-width:\s*(\d+)px/)
      if (minWidth) {
        return width >= Number(minWidth[1])
      }
      return false
    })()

    const mediaQueryList: MediaQueryList = {
      matches,
      media: query,
      onchange: null,
      addListener: () => {},
      removeListener: () => {},
      addEventListener: () => {},
      removeEventListener: () => {},
      dispatchEvent: () => true,
    }

    return mediaQueryList
  }

const setViewport = (width: number, height: number) => {
  Object.assign(window, { innerWidth: width, innerHeight: height })
  window.matchMedia = createMatchMedia(width, height)
}

const renderTestPage = () =>
  render(
    <PageShell
      header={<div>En-tête</div>}
      nav={<MobileActionBar scan={{}} restart={{}} complete={{}} />}
      mainClassName="flex flex-col"
    >
      <div>Contenu principal</div>
    </PageShell>,
  )

describe('Responsive layout smoke tests', () => {
  beforeEach(() => {
    cleanup()
    vi.clearAllMocks()
    document.body.style.overflow = ''
  })

  afterEach(() => {
    cleanup()
  })

  it.each([
    ['iphone-se-portrait', 375, 667],
    ['iphone-se-landscape', 667, 375],
    ['ipad-landscape', 1024, 768],
  ])('renders page shell without horizontal scroll on %s', (_label, width, height) => {
    setViewport(width, height)
    renderTestPage()

    expect(window.getComputedStyle(document.body).overflowX).not.toBe('scroll')
  })

  it('exposes mobile bottom bar on small screens', () => {
    setViewport(375, 667)
    renderTestPage()

    const bottomBar = document.querySelector('.mobile-bottom-bar')
    expect(bottomBar).not.toBeNull()
  })

  it('renders conflict modal full screen with scrollable body on compact viewport', async () => {
    setViewport(375, 667)

    const zone: ConflictZoneSummary = {
      locationId: 'zone-1',
      locationCode: 'A1',
      locationLabel: 'Réserve',
      conflictLines: 1,
    }

    const detail: ConflictZoneDetail = {
      locationId: 'zone-1',
      locationCode: 'A1',
      locationLabel: 'Réserve',
      runs: [
        { runId: 'run-1', countType: 1, completedAtUtc: new Date().toISOString(), ownerDisplayName: 'Camille' },
        { runId: 'run-2', countType: 2, completedAtUtc: new Date().toISOString(), ownerDisplayName: 'Alex' },
      ],
      items: [
        {
          productId: 'prod-1',
          ean: '1234567890123',
          qtyC1: 5,
          qtyC2: 3,
          delta: 2,
          allCounts: [
            { runId: 'run-1', countType: 1, quantity: 5 },
            { runId: 'run-2', countType: 2, quantity: 3 },
          ],
        },
      ],
    }

    vi.mocked(getConflictZoneDetail).mockResolvedValueOnce(detail)

    render(<ConflictZoneModal open zone={zone} onClose={() => {}} />)

    await waitFor(() => expect(getConflictZoneDetail).toHaveBeenCalled())
    await screen.findByText(/Comptage 1/i)

    const modal = screen.getByRole('dialog')
    expect(modal.dataset.modalContainer).toBe('')
    expect(modal.style.maxHeight).toContain('calc(100vh')
    const modalStyles = window.getComputedStyle(modal)
    expect(modalStyles.borderRadius).not.toBe('0px')

    const body = modal.querySelector('[data-conflict-modal-body]') as HTMLElement
    expect(body).not.toBeNull()
    const overflowY = window.getComputedStyle(body).overflowY
    expect(['auto', 'scroll']).toContain(overflowY)
  })

  it('transforms completed runs table into cards on narrow screens', async () => {
    setViewport(375, 667)

    const summary: CompletedRunSummary = {
      runId: 'run-1',
      locationId: 'zone-1',
      locationCode: 'A1',
      locationLabel: 'Réserve',
      countType: 1,
      ownerDisplayName: 'Camille',
      ownerUserId: 'user-1',
      startedAtUtc: new Date().toISOString(),
      completedAtUtc: new Date().toISOString(),
    }

    const detail: CompletedRunDetail = {
      runId: 'run-1',
      locationId: 'zone-1',
      locationCode: 'A1',
      locationLabel: 'Réserve',
      countType: 1,
      ownerDisplayName: 'Camille',
      ownerUserId: 'user-1',
      startedAtUtc: new Date().toISOString(),
      completedAtUtc: new Date().toISOString(),
      items: [
        { productId: 'prod-1', sku: 'SKU-1', name: 'Produit test', ean: '1234567890123', quantity: 5 },
      ],
    }

    vi.mocked(getCompletedRunDetail).mockResolvedValue(detail)

    render(<CompletedRunsModal open completedRuns={[summary]} onClose={() => {}} />)

    const selectButton = await screen.findByRole('button', { name: /Comptage n°1/i })
    await userEvent.click(selectButton)

    await screen.findByText('Produit test')

    const table = screen.getByRole('table')
    expect(table.classList.contains('table')).toBe(true)
    expect(table.querySelectorAll('.table-label').length).toBeGreaterThan(0)
  })
})
