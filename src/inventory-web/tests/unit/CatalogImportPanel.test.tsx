import { render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('../../src/app/api/inventoryApi', () => ({
  fetchInventorySummary: vi.fn(),
}))

vi.mock('../../src/state/ShopContext', () => ({
  useShop: () => ({
    shop: { id: 'shop-1', name: 'Boutique test', kind: 'boutique' },
    setShop: vi.fn(),
    isLoaded: true,
  }),
}))

import { fetchInventorySummary } from '../../src/app/api/inventoryApi'
import type { InventorySummary } from '../../src/app/types/inventory'
import { CatalogImportPanel } from '../../src/app/pages/admin/AdminLocationsPage'

const mockedFetchInventorySummary = vi.mocked(fetchInventorySummary)

const baseSummary: InventorySummary = {
  activeSessions: 0,
  openRuns: 0,
  completedRuns: 0,
  conflicts: 0,
  lastActivityUtc: null,
  openRunDetails: [],
  completedRunDetails: [],
  conflictZones: [],
}

describe('CatalogImportPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockedFetchInventorySummary.mockResolvedValue({ ...baseSummary })
  })

  it("désactive le mode remplacement lorsqu'un comptage est en cours", async () => {
    mockedFetchInventorySummary.mockResolvedValueOnce({ ...baseSummary, openRuns: 2 })

    render(<CatalogImportPanel description="" />)

    await waitFor(() => expect(mockedFetchInventorySummary).toHaveBeenCalled())

    const replaceRadios = await screen.findAllByLabelText('Remplacer le catalogue')
    const replaceRadio = replaceRadios[replaceRadios.length - 1]
    expect(replaceRadio).toBeDisabled()

    const mergeRadios = await screen.findAllByLabelText('Ajouter les produits')
    const mergeRadio = mergeRadios[mergeRadios.length - 1]
    expect(mergeRadio).toBeChecked()

    await screen.findByText('2 comptages sont en cours : le mode ajout est appliqué automatiquement.')
  })

  it("laisse le mode remplacement disponible quand aucun comptage n'est ouvert", async () => {
    render(<CatalogImportPanel description="" />)

    await waitFor(() => expect(mockedFetchInventorySummary).toHaveBeenCalled())

    const replaceRadios = await screen.findAllByLabelText('Remplacer le catalogue')
    const replaceRadio = replaceRadios[replaceRadios.length - 1]
    expect(replaceRadio).not.toBeDisabled()
    expect(replaceRadio).toBeChecked()

    const mergeRadios = await screen.findAllByLabelText('Ajouter les produits')
    const mergeRadio = mergeRadios[mergeRadios.length - 1]
    expect(mergeRadio).not.toBeDisabled()
    expect(mergeRadio).not.toBeChecked()
  })

  it("impose le mode ajout lorsqu'un comptage terminé existe", async () => {
    mockedFetchInventorySummary.mockResolvedValueOnce({ ...baseSummary, completedRuns: 2 })

    render(<CatalogImportPanel description="" />)

    await waitFor(() => expect(mockedFetchInventorySummary).toHaveBeenCalled())

    const replaceRadios = await screen.findAllByLabelText('Remplacer le catalogue')
    const replaceRadio = replaceRadios[replaceRadios.length - 1]
    expect(replaceRadio).toBeDisabled()

    const mergeRadios = await screen.findAllByLabelText('Ajouter les produits')
    const mergeRadio = mergeRadios[mergeRadios.length - 1]
    expect(mergeRadio).toBeChecked()

    await screen.findByText('2 comptages terminés utilisent ce catalogue : le mode ajout est appliqué automatiquement.')
  })
})
