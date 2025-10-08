import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, beforeEach } from 'vitest'
import { http, HttpResponse } from 'msw'

import { renderWithProviders } from '@/test-utils'
import type { Shop } from '@/types/shop'
import type { ShopUser } from '@/types/user'
import type { Location } from '@/app/types/inventory'
import { CountType } from '@/app/types/inventory'
import { InventorySessionPage } from '../InventorySessionPage'
import { server } from '../../../../../tests/msw/server'

const shop: Shop = { id: 'shop-test', name: 'Boutique Test' }

const owner: ShopUser = {
  id: 'user-test',
  shopId: shop.id,
  login: 'user.test',
  displayName: 'Utilisateur Test',
  isAdmin: false,
  disabled: false,
}

const location: Location = {
  id: 'location-test',
  code: 'Z01',
  label: 'Zone test',
  isBusy: false,
  busyBy: null,
  activeRunId: null,
  activeCountType: null,
  activeStartedAtUtc: null,
  countStatuses: [
    {
      countType: 1,
      status: 'completed',
      runId: 'run-c1',
      ownerDisplayName: 'Utilisateur 1',
      ownerUserId: 'user-c1',
      startedAtUtc: null,
      completedAtUtc: null,
    },
    {
      countType: 2,
      status: 'completed',
      runId: 'run-c2',
      ownerDisplayName: 'Utilisateur 2',
      ownerUserId: 'user-c2',
      startedAtUtc: null,
      completedAtUtc: null,
    },
    {
      countType: 3,
      status: 'not_started',
      runId: null,
      ownerDisplayName: null,
      ownerUserId: null,
      startedAtUtc: null,
      completedAtUtc: null,
    },
  ],
}

const baseInventory = {
  selectedUser: owner,
  countType: CountType.Count1,
  location,
  sessionId: null,
  items: [],
}

const startRunHandler = http.post(`/api/inventories/${location.id}/start`, async () =>
  HttpResponse.json({
    runId: 'run-new',
    inventorySessionId: 'session-new',
    locationId: location.id,
    countType: CountType.Count1,
    ownerDisplayName: owner.displayName,
    ownerUserId: owner.id,
    startedAtUtc: new Date().toISOString(),
  }),
)

const releaseRunHandler = http.post(`/api/inventories/${location.id}/release`, async () => HttpResponse.json(null))

const completeRunHandler = http.post(`/api/inventories/${location.id}/complete`, async () =>
  HttpResponse.json({
    runId: 'run-new',
    inventorySessionId: 'session-new',
    locationId: location.id,
    countType: CountType.Count1,
    completedAtUtc: new Date().toISOString(),
    itemsCount: 1,
    totalQuantity: 1,
  }),
)

const setupProductHandler = (ean: string, productName: string) =>
  http.get(`/api/products/${ean}`, async () =>
    HttpResponse.json({
      ean,
      name: productName,
    }),
  )

const setupNotFoundProductHandler = (ean: string) =>
  http.get(`/api/products/${ean}`, async () => HttpResponse.json({ message: 'Not Found' }, { status: 404 }))

const renderSessionPage = () =>
  renderWithProviders(<InventorySessionPage />, {
    route: '/inventory/session',
    shop,
    inventory: baseInventory,
  })

describe('InventorySessionPage', () => {
  beforeEach(() => {
    server.resetHandlers()
    server.use(startRunHandler, releaseRunHandler, completeRunHandler)
  })

  it('permet de scanner un EAN et d’enregistrer l’action dans le journal', async () => {
    const ean = '32165498'
    server.use(setupProductHandler(ean, 'Popcorn caramel'))

    const user = userEvent.setup()
    renderSessionPage()

    const input = await screen.findByLabelText(/Scanner \(douchette ou saisie\)/i)
    await user.type(input, ean)
    await user.keyboard('{Enter}')

    await screen.findByText('Popcorn caramel')
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /Terminer ce comptage/i })).toBeEnabled(),
    )

    const status = await screen.findByTestId('status-message')
    expect(status).toHaveTextContent('Popcorn caramel ajouté')

    const journalButton = screen.getByRole('button', { name: /Journal des actions/i })
    await user.click(journalButton)

    const logsList = await screen.findByTestId('logs-list')
    expect(within(logsList).getByText(/Popcorn caramel .*EAN 32165498/)).toBeInTheDocument()
  })

  it('autorise le passage en mode caméra via le bouton dédié', async () => {
    const user = userEvent.setup()
    const { history } = renderWithProviders(<InventorySessionPage />, {
      route: '/inventory/session',
      shop,
      inventory: baseInventory,
      captureHistory: true,
    })

    const button = await screen.findByRole('button', { name: /Scan caméra/i })
    await user.click(button)

    await waitFor(() => {
      expect(history.at(-1)?.pathname).toBe('/inventory/scan-camera')
    })
  })

  it('propose l’ajout manuel lorsqu’un produit est introuvable et l’ajoute à la liste', async () => {
    const ean = '12345678'
    server.use(setupNotFoundProductHandler(ean))

    const user = userEvent.setup()
    renderSessionPage()

    const input = await screen.findByLabelText(/Scanner \(douchette ou saisie\)/i)
    await user.type(input, ean)
    await user.keyboard('{Enter}')

    const manualButton = await screen.findByRole('button', { name: /Ajouter manuellement/i })
    await waitFor(() => expect(manualButton).toBeEnabled())
    await user.click(manualButton)

    await screen.findByText(`Produit inconnu EAN ${ean}`)
    const status = await screen.findByTestId('status-message')
    expect(status).toHaveTextContent(`Produit inconnu EAN ${ean} ajouté manuellement`)
  })

  it('permet de modifier les quantités et supprime l’article lorsque la quantité retombe à zéro', async () => {
    const ean = '98765432'
    server.use(setupProductHandler(ean, 'Blu-ray culte'))

    const user = userEvent.setup()
    renderSessionPage()

    const input = await screen.findByLabelText(/Scanner \(douchette ou saisie\)/i)
    await user.type(input, ean)
    await user.keyboard('{Enter}')

    const resolveItem = () => {
      const card = screen.getByText('Blu-ray culte')
      const container = card.closest('[data-testid="scanned-item"]')
      if (!container) {
        throw new Error('Article scanné introuvable')
      }
      return container as HTMLElement
    }

    const getQuantityInput = () =>
      within(resolveItem()).getByLabelText(/Quantité pour Blu-ray culte/i)

    const quantityInput = getQuantityInput()
    await user.click(quantityInput)
    await waitFor(() => {
      expect(quantityInput.selectionStart).toBe(0)
      expect(quantityInput.selectionEnd).toBe(quantityInput.value.length)
    })
    await user.type(quantityInput, '4', {
      initialSelectionStart: 0,
      initialSelectionEnd: quantityInput.value.length,
    })
    await user.keyboard('{Enter}')
    await waitFor(() => {
      const currentInput = screen.queryByLabelText(/Quantité pour Blu-ray culte/i) as HTMLInputElement | null
      expect(currentInput).not.toBeNull()
      expect(currentInput!.value).toBe('4')
    })

    const incrementButton = within(resolveItem()).getByRole('button', { name: 'Ajouter' })
    await user.click(incrementButton)
    await waitFor(() => {
      const currentInput = screen.queryByLabelText(/Quantité pour Blu-ray culte/i) as HTMLInputElement | null
      expect(currentInput).not.toBeNull()
      expect(currentInput!.value).toBe('5')
    })

    const decrementButton = within(resolveItem()).getByRole('button', { name: 'Retirer' })
    await user.click(decrementButton)
    await user.click(decrementButton)
    await user.click(decrementButton)
    await user.click(decrementButton)
    await user.click(decrementButton)

    await waitFor(() => expect(screen.queryByText('Blu-ray culte')).not.toBeInTheDocument())
    expect(screen.queryByRole('button', { name: /Terminer ce comptage/i })).not.toBeInTheDocument()

    const journalButton = screen.getByRole('button', { name: /Journal des actions/i })
    await user.click(journalButton)
    const logsList = await screen.findByTestId('logs-list')
    expect(within(logsList).getAllByText(/Quantité mise à jour pour Blu-ray culte .*→ 4/)).not.toHaveLength(0)
    expect(within(logsList).getByText(/retiré de la session/i)).toBeInTheDocument()
  })
})
