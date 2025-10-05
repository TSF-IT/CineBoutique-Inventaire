import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ThemeProvider } from '../../../../theme/ThemeProvider'
import { AdminLocationsPage } from '../AdminLocationsPage'
import type { Location } from '../../../types/inventory'
import { fetchLocations } from '../../../api/inventoryApi'
import { createLocation, updateLocation } from '../../../api/adminApi'
import { ShopProvider } from '@/state/ShopContext'

vi.mock('../../../api/inventoryApi', () => ({
  fetchLocations: vi.fn(),
}))

vi.mock('../../../api/adminApi', () => ({
  createLocation: vi.fn(),
  updateLocation: vi.fn(),
}))

const { fetchLocations: mockedFetchLocations } = vi.mocked({ fetchLocations })
const { createLocation: mockedCreateLocation, updateLocation: mockedUpdateLocation } = vi.mocked({
  createLocation,
  updateLocation,
})

const testShop = { id: 'shop-123', name: 'Boutique test' } as const

const renderAdminPage = async () => {
  render(
    <ThemeProvider>
      <ShopProvider>
        <AdminLocationsPage />
      </ShopProvider>
    </ThemeProvider>,
  )

  await waitFor(() => {
    expect(mockedFetchLocations).toHaveBeenCalledWith(testShop.id)
  })

}

describe('AdminLocationsPage', () => {
  const baseLocation: Location = {
    id: 'loc-1',
    code: 'A01',
    label: 'Réserve',
    isBusy: false,
    busyBy: null,
    activeRunId: null,
    activeCountType: null,
    activeStartedAtUtc: null,
    countStatuses: [],
  }

  beforeEach(() => {
    window.localStorage.clear()
    window.localStorage.setItem('cb.shop', JSON.stringify(testShop))
    mockedFetchLocations.mockReset()
    mockedFetchLocations.mockResolvedValue([baseLocation])
    mockedCreateLocation.mockReset()
    mockedUpdateLocation.mockReset()
  })

  it('crée une nouvelle zone', async () => {
    const createdLocation: Location = {
      ...baseLocation,
      id: 'loc-2',
      code: 'B02',
      label: 'Comptoir',
    }

    mockedCreateLocation.mockResolvedValue(createdLocation)

    await renderAdminPage()

    const user = userEvent.setup()
    const codeInput = (await screen.findAllByLabelText('Code'))[0]
    const labelInput = (await screen.findAllByLabelText('Libellé'))[0]

    await user.clear(codeInput)
    await user.type(codeInput, 'b02')
    await user.clear(labelInput)
    await user.type(labelInput, 'Comptoir')

    const createForm = codeInput.closest('form')
    expect(createForm).not.toBeNull()
    if (!createForm) {
      throw new Error('Formulaire de création introuvable')
    }
    const addButton = within(createForm).getByRole('button', { name: 'Ajouter' })
    await user.click(addButton)

    expect(mockedCreateLocation).toHaveBeenCalledWith({ code: 'B02', label: 'Comptoir' })
    expect(await screen.findByText('Zone créée avec succès.')).toBeInTheDocument()
    expect(screen.getByText('Comptoir')).toBeInTheDocument()
  })

  it('met à jour le code et le libellé d’une zone existante', async () => {
    const updatedLocation: Location = {
      ...baseLocation,
      code: 'C01',
      label: 'Accueil',
    }

    mockedUpdateLocation.mockResolvedValue(updatedLocation)

    await renderAdminPage()

    const user = userEvent.setup()
    const editButtons = await screen.findAllByRole('button', { name: 'Modifier' })
    await user.click(editButtons[0])

    const codeInput = screen.getByDisplayValue('A01')
    const labelInput = screen.getByDisplayValue('Réserve')

    await user.clear(codeInput)
    await user.type(codeInput, 'c01')
    await user.clear(labelInput)
    await user.type(labelInput, 'Accueil')

    const editForm = codeInput.closest('form')
    expect(editForm).not.toBeNull()
    if (!editForm) {
      throw new Error('Formulaire d’édition de zone introuvable')
    }

    const saveButton = within(editForm).getByRole('button', { name: 'Enregistrer' })
    await user.click(saveButton)

    expect(mockedUpdateLocation).toHaveBeenCalledWith(baseLocation.id, { code: 'C01', label: 'Accueil' })
    expect(await screen.findByText('Zone mise à jour.')).toBeInTheDocument()
    expect(screen.getByText('Accueil')).toBeInTheDocument()
    expect(screen.getByText('C01')).toBeInTheDocument()
  })
})
