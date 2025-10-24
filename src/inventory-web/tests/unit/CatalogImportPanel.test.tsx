import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
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

  it('affiche un récapitulatif détaillé après un import réussi', async () => {
    const fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValue({
        status: 200,
        text: async () =>
          JSON.stringify({ total: 500, inserted: 46, updated: 454, errorCount: 0, unknownColumns: [] }),
      } as unknown as Response)

    try {
      render(<CatalogImportPanel description="" />)

      await waitFor(() => expect(mockedFetchInventorySummary).toHaveBeenCalled())

      const fileInputs = screen.getAllByLabelText('Fichier CSV', { selector: 'input' })
      const fileInput = fileInputs[fileInputs.length - 1] as HTMLInputElement
      const file = new File(['sku;item;descr'], 'catalog.csv', { type: 'text/csv' })
      await userEvent.upload(fileInput, file)

      const submitButtons = screen.getAllByRole('button', { name: /remplacer le catalogue/i })
      const submitButton = submitButtons[submitButtons.length - 1]
      await userEvent.click(submitButton)

      await screen.findByText('Import terminé avec succès.')
      await screen.findByText('500 produits dans le fichier : 46 ajoutés, 454 déjà présents.')
      await screen.findByText('Déjà présents')
    } finally {
      fetchSpy.mockRestore()
    }
  })
})
