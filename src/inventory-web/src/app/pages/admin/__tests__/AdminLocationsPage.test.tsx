import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
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
  disableLocation,
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
  disableLocation: vi.fn(),
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
  disableLocation: mockedDisableLocation,
  createShopUser: mockedCreateShopUser,
  updateShopUser: mockedUpdateShopUser,
  disableShopUser: mockedDisableShopUser,
} = vi.mocked({ createLocation, updateLocation, disableLocation, createShopUser, updateShopUser, disableShopUser })
const { fetchShopUsers: mockedFetchShopUsers } = vi.mocked({ fetchShopUsers })

const testShop = { id: 'shop-123', name: 'Boutique test', kind: 'boutique' } as const

const originalFetch = global.fetch
let fetchStatusMock: ReturnType<typeof vi.fn>

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

  const [shopIdArg, optionsArg] = mockedFetchLocations.mock.calls[0] ?? []
  expect(shopIdArg).toBe(testShop.id)
  expect(optionsArg).toEqual({ includeDisabled: true })
}

const openUsersTab = async (user: ReturnType<typeof userEvent.setup>) => {
  const tablists = await screen.findAllByRole('tablist', { name: /choix de la section d'administration/i })
  const tablist = tablists[tablists.length - 1]
  const tabs = within(tablist).getAllByRole('tab')
  const target = tabs.find((tab) => tab.textContent?.includes('Utilisateurs')) ?? tabs[tabs.length - 1]
  await user.click(target as HTMLButtonElement)
}

const openCatalogTab = async (user: ReturnType<typeof userEvent.setup>) => {
  const tabs = await screen.findAllByRole('tab', { name: /^Produits$/i })
  if (tabs.length === 0) {
    throw new Error('Onglet "Produits" introuvable')
  }
  await user.click(tabs[0] as HTMLButtonElement)
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
    disabled: false,
  }

  beforeEach(() => {
    window.localStorage.clear()
    window.localStorage.setItem('cb.shop', JSON.stringify(testShop))
    mockedFetchLocations.mockReset()
    mockedFetchLocations.mockResolvedValue([baseLocation])
    mockedCreateLocation.mockReset()
    mockedUpdateLocation.mockReset()
    mockedDisableLocation.mockReset()
    mockedFetchShopUsers.mockReset()
    mockedFetchShopUsers.mockResolvedValue([])
    mockedCreateShopUser.mockReset()
    mockedUpdateShopUser.mockReset()
    mockedDisableShopUser.mockReset()

    fetchStatusMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ canReplace: true, lockReason: null, hasCountLines: false }),
    } as Response)
    global.fetch = fetchStatusMock as unknown as typeof global.fetch
  })

  afterEach(() => {
    global.fetch = originalFetch
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

  it('désactive une zone et la masque par défaut', async () => {
    mockedDisableLocation.mockResolvedValue({ ...baseLocation, disabled: true })

    await renderAdminPage()

    const user = userEvent.setup()
    const locationCard = await screen.findByTestId('location-card')
    const disableButton = within(locationCard).getByRole('button', { name: 'Désactiver' })
    await user.click(disableButton)

    const confirmDialog = await screen.findByRole('dialog', { name: `Désactiver ${baseLocation.code} ?` })
    const confirmButton = within(confirmDialog).getByRole('button', { name: 'Confirmer la désactivation' })
    await user.click(confirmButton)

    expect(mockedDisableLocation).toHaveBeenCalledWith(baseLocation.id)
    expect(await screen.findByText('Zone désactivée.')).toBeInTheDocument()
    await waitFor(() => {
      expect(screen.queryByTestId('location-card')).not.toBeInTheDocument()
    })

    const toggle = screen.getByLabelText('Masquer les zones désactivées')
    await user.click(toggle)

    const disabledCard = await screen.findByTestId('location-card')
    expect(within(disabledCard).getByText('Désactivée')).toBeInTheDocument()
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
      expect(mockedFetchShopUsers).toHaveBeenCalledWith(testShop.id, { includeDisabled: true })
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
      expect(mockedFetchShopUsers).toHaveBeenCalledWith(testShop.id, { includeDisabled: true })
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
      expect(mockedFetchShopUsers).toHaveBeenCalledWith(testShop.id, { includeDisabled: true })
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
        expect(mockedFetchShopUsers).toHaveBeenCalledWith(testShop.id, { includeDisabled: true })
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

  it('verrouille le remplacement du catalogue quand des comptages existent', async () => {
    const lockedFetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ canReplace: false, lockReason: 'counting_started', hasCountLines: true }),
    } as Response)

    const previousFetch = global.fetch
    global.fetch = lockedFetch as unknown as typeof global.fetch

    try {
      await renderAdminPage()

      const user = userEvent.setup()
      await openCatalogTab(user)

      await waitFor(() => {
        expect(lockedFetch).toHaveBeenCalledWith(
          `/api/shops/${testShop.id}/products/import/status`
        )
      })

      const replaceRadio = await screen.findByRole('radio', { name: /Remplacer le catalogue/i })
      expect(replaceRadio).toBeDisabled()

      const mergeRadio = screen.getByRole('radio', { name: /Compléter le catalogue/i })
      expect(mergeRadio).toBeChecked()
      expect(mergeRadio).not.toBeDisabled()

      expect(await screen.findByText(/Remplacement verrouillé/i)).toBeInTheDocument()
    } finally {
      global.fetch = previousFetch
    }
  })

  it("ajoute le paramètre de mode 'merge' lors d'un import complémentaire", async () => {
    const previousFetch = global.fetch
    const recordedCalls: { url: string; _init?: RequestInit }[] = []

    const fetchMock = vi.fn().mockImplementation(
      async (input: RequestInfo | URL, __init?: RequestInit): Promise<Response> => {
        const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url
        recordedCalls.push({ url, _init: __init })

        if (url.endsWith('/products/import/status')) {
          return {
            ok: true,
            status: 200,
            json: async () => ({ canReplace: true, lockReason: null, hasCountLines: false }),
          } as Response
        }

        if (url.includes('/products/import')) {
          return {
            ok: true,
            status: 200,
            text: async () => JSON.stringify({ inserted: 12, errorCount: 0 }),
          } as Response
        }

        throw new Error(`Unexpected fetch: ${url}`)
      },
    )

    global.fetch = fetchMock as unknown as typeof global.fetch

    try {
      await renderAdminPage()

      const user = userEvent.setup()
      await openCatalogTab(user)

      await screen.findByRole('radio', { name: /Remplacer le catalogue/i })

      const file = new File(['sku;ean;name'], 'catalog.csv', { type: 'text/csv' })
      const fileInputs = screen.getAllByLabelText('Fichier CSV', { selector: 'input[type="file"]' })
      const fileInput = fileInputs[fileInputs.length - 1]
      await user.upload(fileInput, file)

      const mergeRadio = screen.getByRole('radio', { name: /Compléter le catalogue/i })
      await user.click(mergeRadio)

      const submitButton = screen.getByRole('button', { name: 'Importer en complément' })
      await user.click(submitButton)

      await waitFor(() => {
        expect(fetchMock).toHaveBeenCalledWith(expect.stringContaining('/products/import?'), expect.anything())
      })

    const importCall = recordedCalls.find(({ url }) => url.includes('/products/import?'))
      expect(importCall).toBeDefined()
      expect(importCall?.url).toContain('mode=merge')
    expect(importCall?._init?.method ?? 'POST').toBe('POST')
    } finally {
      global.fetch = previousFetch
    }
  })

  it("utilise le mode 'replace' par défaut lorsque le remplacement est autorisé", async () => {
    const previousFetch = global.fetch
    const fetchMock = vi.fn().mockImplementation(
      async (input: RequestInfo | URL, _init?: RequestInit): Promise<Response> => {
        const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url

        if (url.endsWith('/products/import/status')) {
          return {
            ok: true,
            status: 200,
            json: async () => ({ canReplace: true, lockReason: null, hasCountLines: false }),
          } as Response
        }

        if (url.includes('/products/import')) {
          expect(url).toContain('mode=replace')
          return {
            ok: true,
            status: 200,
            text: async () => JSON.stringify({ inserted: 5, errorCount: 0 }),
          } as Response
        }

        throw new Error(`Unexpected fetch: ${url}`)
      },
    )

    global.fetch = fetchMock as unknown as typeof global.fetch

    try {
      await renderAdminPage()

      const user = userEvent.setup()
      await openCatalogTab(user)

      await screen.findByRole('radio', { name: /Remplacer le catalogue/i })

      const file = new File(['sku;ean;name'], 'catalog.csv', { type: 'text/csv' })
      const fileInputs = screen.getAllByLabelText('Fichier CSV', { selector: 'input[type="file"]' })
      const fileInput = fileInputs[fileInputs.length - 1]
      await user.upload(fileInput, file)

      const formElement = fileInput.closest('form')
      expect(formElement).not.toBeNull()
      formElement?.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))

      await waitFor(() => {
        expect(fetchMock).toHaveBeenCalledWith(expect.stringContaining('/products/import?'), expect.anything())
      })
    } finally {
      global.fetch = previousFetch
    }
  })
})
