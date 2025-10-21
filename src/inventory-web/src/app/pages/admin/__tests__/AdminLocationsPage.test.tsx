import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ThemeProvider } from '../../../../theme/ThemeProvider'
import { AdminLocationsPage } from '../AdminLocationsPage'
import type { Location } from '../../../types/inventory'
import type { ShopUser } from '@/types/user'
import { fetchLocations } from '../../../api/inventoryApi'
import {
  createLocation,
  updateLocation,
  createShopUser,
  updateShopUser,
  disableShopUser,
} from '../../../api/adminApi'
import { fetchShopUsers } from '../../../api/shopUsers'
import { ShopProvider } from '@/state/ShopContext'

vi.mock('../../../api/inventoryApi', () => ({
  fetchLocations: vi.fn(),
}))

vi.mock('../../../api/adminApi', () => ({
  createLocation: vi.fn(),
  updateLocation: vi.fn(),
  createShopUser: vi.fn(),
  updateShopUser: vi.fn(),
  disableShopUser: vi.fn(),
}))

vi.mock('../../../api/shopUsers', () => ({
  fetchShopUsers: vi.fn(),
}))

const { fetchLocations: mockedFetchLocations } = vi.mocked({ fetchLocations })
const {
  createLocation: mockedCreateLocation,
  updateLocation: mockedUpdateLocation,
  createShopUser: mockedCreateShopUser,
  updateShopUser: mockedUpdateShopUser,
  disableShopUser: mockedDisableShopUser,
} = vi.mocked({ createLocation, updateLocation, createShopUser, updateShopUser, disableShopUser })
const { fetchShopUsers: mockedFetchShopUsers } = vi.mocked({ fetchShopUsers })

const testShop = { id: 'shop-123', name: 'Boutique test', kind: 'boutique' } as const

const renderAdminPage = async () => {
  render(
    <ThemeProvider>
      <ShopProvider>
        <AdminLocationsPage />
      </ShopProvider>
    </ThemeProvider>,
  )

  await waitFor(() => {
    expect(mockedFetchLocations).toHaveBeenCalled()
  })

  expect(mockedFetchLocations.mock.calls[0]?.[0]).toBe(testShop.id)

}

const openUsersTab = async (user: ReturnType<typeof userEvent.setup>) => {
  const tablists = await screen.findAllByRole('tablist', { name: /choix de la section d'administration/i })
  const tablist = tablists[tablists.length - 1]
  const tabs = within(tablist).getAllByRole('tab')
  const target = tabs.find((tab) => tab.textContent?.includes('Utilisateurs')) ?? tabs[tabs.length - 1]
  await user.click(target as HTMLButtonElement)
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
    mockedFetchShopUsers.mockReset()
    mockedFetchShopUsers.mockResolvedValue([])
    mockedCreateShopUser.mockReset()
    mockedUpdateShopUser.mockReset()
    mockedDisableShopUser.mockReset()
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
    expect(mockedFetchShopUsers).not.toHaveBeenCalled()
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
    expect(mockedFetchShopUsers).not.toHaveBeenCalled()
  })

  it("charge les utilisateurs lors de l'ouverture de l'onglet dédié", async () => {
    await renderAdminPage()

    expect(mockedFetchShopUsers).not.toHaveBeenCalled()

    const user = userEvent.setup()
    await openUsersTab(user)

    await waitFor(() => {
      expect(mockedFetchShopUsers).toHaveBeenCalledWith(testShop.id)
    })
  })

  it('crée un nouvel utilisateur', async () => {
    const createdUser: ShopUser = {
      id: 'user-2',
      shopId: testShop.id,
      login: 'camille',
      displayName: 'Camille Dupont',
      isAdmin: true,
      disabled: false,
    }

    mockedCreateShopUser.mockResolvedValue(createdUser)

    await renderAdminPage()

    const user = userEvent.setup()
    await openUsersTab(user)

    await waitFor(() => {
      expect(mockedFetchShopUsers).toHaveBeenCalledWith(testShop.id)
    })

    const createForm = (await screen.findAllByTestId('user-create-form'))[0] as HTMLElement
    const loginInput = within(createForm).getByLabelText('Identifiant') as HTMLInputElement
    const displayNameInput = within(createForm).getByLabelText('Nom affiché') as HTMLInputElement
    const adminCheckbox = within(createForm).getAllByLabelText('Administrateur')[0]

    await user.type(loginInput, 'camille')
    await user.type(displayNameInput, 'Camille Dupont')
    await user.click(adminCheckbox)

    const addButton = within(createForm).getByRole('button', { name: 'Ajouter' })
    await user.click(addButton)

    expect(mockedCreateShopUser).toHaveBeenCalledWith(testShop.id, {
      login: 'camille',
      displayName: 'Camille Dupont',
      isAdmin: true,
    })

    expect(await screen.findByText('Utilisateur créé avec succès.')).toBeInTheDocument()
    const createdCards = await screen.findAllByTestId('user-card')
    const createdCard = createdCards.find((card) => card.getAttribute('data-user-id') === createdUser.id)
    expect(createdCard).toBeDefined()
    if (!createdCard) {
      throw new Error('Carte utilisateur créée introuvable')
    }
    expect(within(createdCard).getByText('Camille Dupont')).toBeInTheDocument()
    expect(within(createdCard).getByText('camille')).toBeInTheDocument()
    expect(within(createdCard).getByText('Administrateur')).toBeInTheDocument()
  })

  it('met à jour un utilisateur existant', async () => {
    const existingUser: ShopUser = {
      id: 'user-1',
      shopId: testShop.id,
      login: 'amelie',
      displayName: 'Amélie Martin',
      isAdmin: false,
      disabled: false,
    }
    const updatedUser: ShopUser = { ...existingUser, displayName: 'Amélie Leroy', isAdmin: true }

    mockedFetchShopUsers.mockResolvedValue([existingUser])
    mockedUpdateShopUser.mockResolvedValue(updatedUser)

    await renderAdminPage()

    const user = userEvent.setup()
    await openUsersTab(user)

    await waitFor(() => {
      expect(mockedFetchShopUsers).toHaveBeenCalledWith(testShop.id)
    })

    const userCards = await screen.findAllByTestId('user-card')
    const targetCard = userCards.find((card) => card.getAttribute('data-user-id') === existingUser.id) as HTMLElement | undefined
    expect(targetCard).toBeDefined()
    if (!targetCard) {
      throw new Error('Carte utilisateur à éditer introuvable')
    }
    const editButton = within(targetCard).getByRole('button', { name: 'Modifier' })
    await user.click(editButton)

    const editForm = targetCard.querySelector('form') as HTMLElement | null
    expect(editForm).not.toBeNull()
    if (!editForm) {
      throw new Error('Formulaire de mise à jour utilisateur introuvable')
    }
    const loginInput = within(editForm).getByDisplayValue('amelie') as HTMLInputElement
    const displayNameInput = within(editForm).getByDisplayValue('Amélie Martin') as HTMLInputElement
    const adminCheckbox = within(editForm).getAllByLabelText('Administrateur')[0]

    await user.clear(loginInput)
    await user.type(loginInput, 'amelie')
    await user.clear(displayNameInput)
    await user.type(displayNameInput, 'Amélie Leroy')
    if (!(adminCheckbox as HTMLInputElement).checked) {
      await user.click(adminCheckbox)
    }

    const saveButton = screen.getByRole('button', { name: 'Enregistrer' })
    await user.click(saveButton)

    expect(mockedUpdateShopUser).toHaveBeenCalledWith(testShop.id, {
      id: existingUser.id,
      login: 'amelie',
      displayName: 'Amélie Leroy',
      isAdmin: true,
    })

    expect(await screen.findByText('Utilisateur mis à jour.')).toBeInTheDocument()
    const updatedCard = await screen.findAllByTestId('user-card')
    const refreshedCard = updatedCard.find((card) => card.getAttribute('data-user-id') === existingUser.id)
    expect(refreshedCard).toBeDefined()
    if (!refreshedCard) {
      throw new Error('Carte utilisateur mise à jour introuvable')
    }
    expect(within(refreshedCard).getByText('Amélie Leroy')).toBeInTheDocument()
    expect(within(refreshedCard).getByText('Administrateur')).toBeInTheDocument()
  })

  it('désactive un utilisateur', async () => {
    const existingUser: ShopUser = {
      id: 'user-3',
      shopId: testShop.id,
      login: 'bruno',
      displayName: 'Bruno Caron',
      isAdmin: false,
      disabled: false,
    }
    const disabledUser: ShopUser = { ...existingUser, disabled: true }

    mockedFetchShopUsers.mockResolvedValue([existingUser])
    mockedDisableShopUser.mockResolvedValue(disabledUser)

    const dialogPrototype = window.HTMLDialogElement?.prototype
    const originalShowModal = dialogPrototype?.showModal
    const originalClose = dialogPrototype?.close
    if (dialogPrototype) {
      dialogPrototype.showModal = function showModal() {
        this.setAttribute('open', 'true')
      }
      dialogPrototype.close = function close() {
        this.removeAttribute('open')
      }
    }

    try {
      await renderAdminPage()

      const user = userEvent.setup()
      await openUsersTab(user)

      await waitFor(() => {
        expect(mockedFetchShopUsers).toHaveBeenCalledWith(testShop.id)
      })

      const userCard = (await screen.findAllByTestId('user-card')).find(
        (card) => card.getAttribute('data-user-id') === existingUser.id,
      ) as HTMLElement | undefined
      expect(userCard).toBeDefined()
      if (!userCard) {
        throw new Error('Carte utilisateur introuvable pour la désactivation')
      }
      const disableButton = within(userCard).getByRole('button', { name: 'Désactiver' })
      await user.click(disableButton)

      const confirmationDialog = await screen.findByRole('dialog', {
        name: `Désactiver ${existingUser.displayName} ?`,
      })
      const confirmButton = within(confirmationDialog).getByRole('button', { name: 'Confirmer la désactivation' })
      await user.click(confirmButton)

      expect(mockedDisableShopUser).toHaveBeenCalledWith(testShop.id, existingUser.id)
      expect(await screen.findByText('Utilisateur désactivé.')).toBeInTheDocument()
      await waitFor(() => {
        expect(screen.queryByText('Bruno Caron')).not.toBeInTheDocument()
      })
    } finally {
      if (dialogPrototype) {
        const prototypeWithOverrides = dialogPrototype as {
          showModal?: (() => void) | undefined
          close?: (() => void) | undefined
        }
        if (originalShowModal) {
          prototypeWithOverrides.showModal = originalShowModal
        } else {
          delete prototypeWithOverrides.showModal
        }
        if (originalClose) {
          prototypeWithOverrides.close = originalClose
        } else {
          delete prototypeWithOverrides.close
        }
      }
    }
  })
})
