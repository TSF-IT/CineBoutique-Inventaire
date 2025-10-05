import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import type { ReactNode } from 'react'
import { useLayoutEffect, useRef, useState } from 'react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AppProviders } from '../providers/AppProviders'
import { InventoryLayout } from '../pages/inventory/InventoryLayout'
import { InventoryUserStep } from '../pages/inventory/InventoryUserStep'
import { InventoryCountTypeStep } from '../pages/inventory/InventoryCountTypeStep'
import { InventoryLocationStep } from '../pages/inventory/InventoryLocationStep'
import { InventorySessionPage } from '../pages/inventory/InventorySessionPage'
import { useInventory } from '../contexts/InventoryContext'
import { CountType } from '../types/inventory'
import type { InventorySummary, Location, CompleteInventoryRunPayload } from '../types/inventory'
import type { StartInventoryRunPayload, StartInventoryRunResponse } from '../api/inventoryApi'
import type { HttpError } from '../../lib/api/http'
import type { ShopUser } from '@/types/user'
import { SELECTED_USER_STORAGE_PREFIX } from '../../lib/selectedUserStorage'

const {
  fetchLocationsMock,
  fetchProductMock,
  fetchInventorySummaryMock,
  completeInventoryRunMock,
  startInventoryRunMock,
  releaseInventoryRunMock,
  fetchShopUsersMock,
  shopUsers,
  reserveLocation,
} = vi.hoisted(() => {
  const reserveLocation: Location = {
    id: 'zone-1',
    code: 'RES',
    label: 'Réserve',
    isBusy: false,
    busyBy: null,
    activeRunId: null,
    activeCountType: null,
    activeStartedAtUtc: null,
    countStatuses: [
      {
        countType: 1,
        status: 'not_started',
        runId: null,
        ownerDisplayName: null,
        ownerUserId: null,
        startedAtUtc: null,
        completedAtUtc: null,
      },
      {
        countType: 2,
        status: 'not_started',
        runId: null,
        ownerDisplayName: null,
        ownerUserId: null,
        startedAtUtc: null,
        completedAtUtc: null,
      },
    ],
  }
  const busyLocation: Location = {
    id: 'zone-2',
    code: 'SAL1',
    label: 'Salle 1',
    isBusy: true,
    busyBy: 'paul.dupont',
    activeCountType: 1,
    activeRunId: 'run-1',
    activeStartedAtUtc: new Date(),
    countStatuses: [
      {
        countType: 1,
        status: 'in_progress',
        runId: 'run-1',
        ownerDisplayName: 'paul.dupont',
        ownerUserId: 'user-lyon-1',
        startedAtUtc: new Date(),
        completedAtUtc: null,
      },
      {
        countType: 2,
        status: 'not_started',
        runId: null,
        ownerDisplayName: null,
        ownerUserId: null,
        startedAtUtc: null,
        completedAtUtc: null,
      },
    ],
  }
  const emptySummary: InventorySummary = {
    activeSessions: 0,
    openRuns: 0,
    completedRuns: 0,
    conflicts: 0,
    lastActivityUtc: null,
    openRunDetails: [],
    completedRunDetails: [],
    conflictZones: [],
  }
  const shopUsers: ShopUser[] = [
    {
      id: 'user-paris',
      shopId: 'shop-123',
      login: 'paris',
      displayName: 'Utilisateur Paris',
      isAdmin: false,
      disabled: false,
    },
    {
      id: 'user-lyon-1',
      shopId: 'shop-123',
      login: 'lyon1',
      displayName: 'Utilisateur Lyon 1',
      isAdmin: false,
      disabled: false,
    },
    {
      id: 'user-lyon-2',
      shopId: 'shop-123',
      login: 'lyon2',
      displayName: 'Utilisateur Lyon 2',
      isAdmin: false,
      disabled: false,
    },
    {
      id: 'user-marseille-1',
      shopId: 'shop-123',
      login: 'marseille1',
      displayName: 'Utilisateur Marseille 1',
      isAdmin: false,
      disabled: false,
    },
    {
      id: 'user-lille',
      shopId: 'shop-123',
      login: 'lille',
      displayName: 'Utilisateur Lille',
      isAdmin: false,
      disabled: false,
    },
  ]
  return {
    fetchLocationsMock: vi.fn(async (shopId: string): Promise<Location[]> => {
      expect(shopId).toBeTruthy()
      return [
        { ...reserveLocation },
        { ...busyLocation },
      ]
    }),
    fetchProductMock: vi.fn(() => Promise.resolve({ ean: '123', name: 'Popcorn caramel' })),
    fetchInventorySummaryMock: vi.fn(async (): Promise<InventorySummary> => ({ ...emptySummary })),
    completeInventoryRunMock: vi.fn<(locationId: string, payload: CompleteInventoryRunPayload) => Promise<{
        runId: string;
        inventorySessionId: string;
        locationId: string;
        countType: number;
        completedAtUtc: string;
        itemsCount: number;
        totalQuantity: number;
      }>>()
      .mockResolvedValue({
        runId: 'run-1',
        inventorySessionId: 'session-1',
        locationId: reserveLocation.id,
        countType: 1,
        completedAtUtc: new Date().toISOString(),
        itemsCount: 1,
        totalQuantity: 1,
      }),
    startInventoryRunMock: vi
      .fn<
        (locationId: string, payload: StartInventoryRunPayload) => Promise<StartInventoryRunResponse>
      >()
      .mockResolvedValue({
        runId: 'run-lock-1',
        inventorySessionId: 'session-lock-1',
        locationId: reserveLocation.id,
        countType: 1,
        ownerDisplayName: shopUsers[0]?.displayName ?? 'Utilisateur Paris',
        ownerUserId: shopUsers[0]?.id ?? 'user-paris',
        startedAtUtc: new Date().toISOString(),
      }),
    releaseInventoryRunMock: vi.fn(async () => {}),
    fetchShopUsersMock: vi.fn(async (shopId: string): Promise<ShopUser[]> => {
      expect(shopId).toBeTruthy()
      return shopUsers
    }),
    shopUsers,
    reserveLocation,
  }
})

const testShop = { id: 'shop-123', name: 'Boutique test' } as const

vi.mock('../api/inventoryApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/inventoryApi')>()
  return {
    ...actual,
    fetchLocations: fetchLocationsMock,
    fetchProductByEan: fetchProductMock,
    fetchInventorySummary: fetchInventorySummaryMock,
    completeInventoryRun: completeInventoryRunMock,
    startInventoryRun: startInventoryRunMock,
    releaseInventoryRun: releaseInventoryRunMock,
  }
})

vi.mock('../api/shopUsers', () => ({
  fetchShopUsers: fetchShopUsersMock,
}))

interface RenderInventoryOptions {
  initialize?: (inventory: ReturnType<typeof useInventory>) => void
}

const renderInventoryRoutes = (initialEntry: string, options?: RenderInventoryOptions) => {
  const initialize = options?.initialize

  const Bootstrapper = ({ children }: { children: ReactNode }) => {
    const inventory = useInventory()
    const initializedRef = useRef(false)
    const [ready, setReady] = useState(!initialize)
    const initializeRef = useRef(initialize)

    useLayoutEffect(() => {
      initializeRef.current = initialize
    })

    useLayoutEffect(() => {
      const initializer = initializeRef.current

      if (initializedRef.current || !initializer) {
        return
      }
      initializer(inventory)
      initializedRef.current = true
      setReady(true)
    }, [inventory])

    if (!ready) {
      return null
    }

    return <>{children}</>
  }

  const routerTree = (
    <MemoryRouter initialEntries={[initialEntry]}>
      <Routes>
        <Route path="/inventory" element={<InventoryLayout />}>
          <Route index element={<InventoryUserStep />} />
          <Route path="start" element={<InventoryUserStep />} />
          <Route path="location" element={<InventoryLocationStep />} />
          <Route path="count-type" element={<InventoryCountTypeStep />} />
          <Route path="session" element={<InventorySessionPage />} />
        </Route>
      </Routes>
    </MemoryRouter>
  )

  return render(
    <AppProviders>
      {initialize ? <Bootstrapper>{routerTree}</Bootstrapper> : routerTree}
    </AppProviders>,
  )
}

describe("Workflow d'inventaire", () => {
  beforeEach(() => {
    localStorage.setItem('cb.shop', JSON.stringify(testShop))
    sessionStorage.clear()
    fetchShopUsersMock.mockReset()
    fetchShopUsersMock.mockResolvedValue(shopUsers)
    fetchLocationsMock.mockReset()
    fetchLocationsMock.mockImplementation(async (shopId: string): Promise<Location[]> => {
      expect(shopId).toBeTruthy()
      return [
        { ...reserveLocation },
        {
          id: 'zone-2',
          code: 'SAL1',
          label: 'Salle 1',
          isBusy: true,
          busyBy: 'paul.dupont',
          activeCountType: 1,
          activeRunId: 'run-1',
          activeStartedAtUtc: new Date(),
          countStatuses: [
            {
              countType: 1,
              status: 'in_progress',
              runId: 'run-1',
              ownerDisplayName: 'paul.dupont',
              ownerUserId: 'user-lyon-1',
              startedAtUtc: new Date(),
              completedAtUtc: null,
            },
            {
              countType: 2,
              status: 'not_started',
              runId: null,
              ownerDisplayName: null,
              ownerUserId: null,
              startedAtUtc: null,
              completedAtUtc: null,
            },
          ],
        },
      ]
    })
    fetchProductMock.mockReset()
    fetchProductMock.mockImplementation(() => Promise.resolve({ ean: '123', name: 'Popcorn caramel' }))
    fetchInventorySummaryMock.mockReset()
    fetchInventorySummaryMock.mockImplementation(async (): Promise<InventorySummary> => ({
      activeSessions: 0,
      openRuns: 0,
      completedRuns: 0,
      conflicts: 0,
      lastActivityUtc: null,
      openRunDetails: [],
      completedRunDetails: [],
      conflictZones: [],
    }))
    completeInventoryRunMock.mockReset()
    completeInventoryRunMock.mockImplementation(async () => ({
      runId: 'run-1',
      inventorySessionId: 'session-1',
      locationId: reserveLocation.id,
      countType: 2,
      completedAtUtc: new Date().toISOString(),
      itemsCount: 1,
      totalQuantity: 1,
    }))
    startInventoryRunMock.mockReset()
    startInventoryRunMock.mockImplementation(async () => ({
      runId: 'run-lock-1',
      inventorySessionId: 'session-lock-1',
      locationId: reserveLocation.id,
      countType: 1,
      ownerDisplayName: shopUsers[0]?.displayName ?? 'Utilisateur Paris',
      ownerUserId: shopUsers[0]?.id ?? 'user-paris',
      startedAtUtc: new Date().toISOString(),
    }))
    releaseInventoryRunMock.mockReset()
    releaseInventoryRunMock.mockResolvedValue()
  })

  it('permet de sélectionner utilisateur, zone et type en respectant les statuts', async () => {
    renderInventoryRoutes('/inventory/start')

    const userButton = await screen.findByRole('button', { name: shopUsers[0].displayName })
    fireEvent.click(userButton)

    const locationPages = await screen.findAllByTestId('page-location')
    expect(locationPages).not.toHaveLength(0)

    await waitFor(() => expect(fetchLocationsMock).toHaveBeenCalledTimes(1))
    expect(fetchLocationsMock.mock.calls[0]?.[0]).toBe(testShop.id)

    await waitFor(() => expect(fetchInventorySummaryMock).toHaveBeenCalledTimes(1))

    const busyZoneCard = await screen.findByTestId('zone-card-zone-2')
    expect(within(busyZoneCard).getByText(/Comptage n°1 en cours/i)).toBeInTheDocument()

    fireEvent.click(within(busyZoneCard).getByTestId('btn-select-zone'))

    const countTypePages = await screen.findAllByTestId('page-count-type')
    const activeCountTypePage = countTypePages[countTypePages.length - 1]

    const countTypeOneButton = within(activeCountTypePage).getByTestId('btn-count-type-1')
    expect(countTypeOneButton).toBeDisabled()

    const countTypeTwoButton = within(activeCountTypePage).getByTestId('btn-count-type-2')
    expect(countTypeTwoButton).not.toBeDisabled()
    fireEvent.click(countTypeTwoButton)

    await waitFor(() =>
      expect(
        screen.getAllByText((content) => content.replace(/\s+/g, ' ').includes('Zone : Salle 1')).length,
      ).toBeGreaterThan(0),
    )
    await waitFor(() =>
      expect(
        screen.getAllByText((content) => content.replace(/\s+/g, ' ').includes('Comptage : 2')).length,
      ).toBeGreaterThan(0),
    )
  })

  it("affiche l'état de conflit pour une zone terminée", async () => {
    const completedZone: Location = {
      id: 'zone-4',
      code: 'ZC4',
      label: 'Zone ZC4',
      isBusy: false,
      busyBy: null,
      activeRunId: null,
      activeCountType: null,
      activeStartedAtUtc: null,
      countStatuses: [
        {
          countType: CountType.Count1,
          status: 'completed',
          runId: 'run-10',
          ownerDisplayName: 'Luc',
          ownerUserId: 'user-luc',
          startedAtUtc: new Date(),
          completedAtUtc: new Date(),
        },
        {
          countType: CountType.Count2,
          status: 'completed',
          runId: 'run-11',
          ownerDisplayName: 'Mila',
          ownerUserId: 'user-mila',
          startedAtUtc: new Date(),
          completedAtUtc: new Date(),
        },
      ],
    }

    fetchLocationsMock.mockResolvedValueOnce([
      { ...reserveLocation },
      completedZone,
    ])

    fetchInventorySummaryMock.mockResolvedValueOnce({
      activeSessions: 0,
      openRuns: 0,
      completedRuns: 0,
      conflicts: 1,
      lastActivityUtc: null,
      openRunDetails: [],
      completedRunDetails: [],
      conflictZones: [
        {
          locationId: completedZone.id,
          locationCode: completedZone.code,
          locationLabel: completedZone.label,
          conflictLines: 1,
        },
      ],
    })

    renderInventoryRoutes('/inventory/start')

    const userButton = await screen.findByRole('button', { name: shopUsers[0].displayName })
    fireEvent.click(userButton)

    const conflictZoneCard = await screen.findByTestId(`zone-card-${completedZone.id}`)
    await waitFor(() => expect(within(conflictZoneCard).getByText('Conflit détecté')).toBeInTheDocument())
    expect(within(conflictZoneCard).queryByText('Aucun conflit')).not.toBeInTheDocument()
  })

  it('propose automatiquement le 3ᵉ comptage pour une zone en conflit', async () => {
    const conflictZone: Location = {
      id: 'zone-5',
      code: 'ZC5',
      label: 'Zone ZC5',
      isBusy: false,
      busyBy: null,
      activeRunId: null,
      activeCountType: null,
      activeStartedAtUtc: null,
      countStatuses: [
        {
          countType: CountType.Count1,
          status: 'completed',
          runId: 'run-21',
          ownerDisplayName: 'Chloé',
          ownerUserId: 'user-chloe',
          startedAtUtc: new Date(),
          completedAtUtc: new Date(),
        },
        {
          countType: CountType.Count2,
          status: 'completed',
          runId: 'run-22',
          ownerDisplayName: 'Utilisateur Nice 1',
          ownerUserId: 'user-nice-1',
          startedAtUtc: new Date(),
          completedAtUtc: new Date(),
        },
      ],
    }

    fetchLocationsMock.mockResolvedValueOnce([
      { ...reserveLocation },
      conflictZone,
    ])

    fetchInventorySummaryMock.mockResolvedValueOnce({
      activeSessions: 0,
      openRuns: 0,
      completedRuns: 0,
      conflicts: 1,
      lastActivityUtc: null,
      openRunDetails: [],
      completedRunDetails: [],
      conflictZones: [
        {
          locationId: conflictZone.id,
          locationCode: conflictZone.code,
          locationLabel: conflictZone.label,
          conflictLines: 3,
        },
      ],
    })

    renderInventoryRoutes('/inventory/start')

    const userButton = await screen.findByRole('button', { name: shopUsers[0].displayName })
    fireEvent.click(userButton)

    const conflictCard = await screen.findByTestId(`zone-card-${conflictZone.id}`)
    const actionButton = within(conflictCard).getByTestId('btn-select-zone')
    expect(actionButton).toBeEnabled()
    expect(actionButton).toHaveTextContent('Lancer le 3ᵉ comptage')

    fireEvent.click(actionButton)

    const sessionPages = await screen.findAllByTestId('page-session')
    const activeSession = sessionPages[sessionPages.length - 1]
    await waitFor(() => expect(within(activeSession).getByText(/3 comptages/i)).toBeInTheDocument())
  })

  it('autorise la reprise de son propre comptage', async () => {
    const selfRunLocation: Location = {
      ...reserveLocation,
      id: 'zone-3',
      code: 'SAL2',
      label: 'Salle 2',
      isBusy: true,
      busyBy: shopUsers[0].displayName,
      activeRunId: 'run-self',
      activeCountType: 1,
      activeStartedAtUtc: new Date(),
      countStatuses: [
        {
          countType: CountType.Count1,
          status: 'in_progress',
          runId: 'run-self',
          ownerDisplayName: shopUsers[0].displayName,
          ownerUserId: shopUsers[0].id,
          startedAtUtc: new Date(),
          completedAtUtc: null,
        },
        reserveLocation.countStatuses[1],
      ],
    }

    fetchLocationsMock.mockResolvedValueOnce([
      { ...reserveLocation },
      selfRunLocation,
    ])

    renderInventoryRoutes('/inventory/start')

    const userButton = await screen.findByRole('button', { name: shopUsers[0].displayName })
    fireEvent.click(userButton)

    const selfZoneCard = await screen.findByTestId('zone-card-zone-3')
    fireEvent.click(within(selfZoneCard).getByTestId('btn-select-zone'))

    const countTypePages = await screen.findAllByTestId('page-count-type')
    const activeCountTypePage = countTypePages[countTypePages.length - 1]

    const countTypeOneButton = within(activeCountTypePage).getByTestId('btn-count-type-1')
    expect(countTypeOneButton).not.toBeDisabled()

    fireEvent.click(countTypeOneButton)

    await waitFor(() =>
      expect(
        screen.getAllByText((content) => content.replace(/\s+/g, ' ').includes('Zone : Salle 2')).length,
      ).toBeGreaterThan(0),
    )
    await waitFor(() =>
      expect(
        screen.getAllByText((content) => content.replace(/\s+/g, ' ').includes('Comptage : 1')).length,
      ).toBeGreaterThan(0),
    )
  })

  it("ajoute automatiquement un produit lorsqu'un EAN connu est saisi", async () => {
    renderInventoryRoutes('/inventory/session', {
      initialize: (inventory) => {
        inventory.setSelectedUser(shopUsers[0])
        inventory.setCountType(CountType.Count1)
        inventory.setLocation({ ...reserveLocation })
        inventory.clearSession()
      },
    })

    const [input] = await screen.findAllByLabelText('Scanner (douchette ou saisie)')
    fireEvent.change(input, { target: { value: '123' } })

    await waitFor(() => expect(fetchProductMock).toHaveBeenCalledWith('123'))
    await waitFor(() => expect(screen.getByText('Popcorn caramel')).toBeInTheDocument())
    await waitFor(() => expect((input as HTMLInputElement).value).toBe(''))
  })

  it("active l'ajout manuel uniquement lorsqu'un EAN est introuvable", async () => {
    const notFoundError: HttpError = {
      name: 'Error',
      message: 'HTTP 404',
      status: 404,
      url: '/api/products/99999999',
    }

    fetchProductMock.mockRejectedValueOnce(notFoundError)

    renderInventoryRoutes('/inventory/session', {
      initialize: (inventory) => {
        inventory.setSelectedUser(shopUsers[0])
        inventory.setCountType(CountType.Count1)
        inventory.setLocation({ ...reserveLocation })
        inventory.clearSession()
      },
    })

    const [input] = await screen.findAllByLabelText('Scanner (douchette ou saisie)')
    const sessionPages = await screen.findAllByTestId('page-session')
    const inputPage = (input as HTMLElement).closest('[data-testid="page-session"]') as HTMLElement | null
    const pageContainer = inputPage ?? sessionPages[sessionPages.length - 1]
    const manualButton = within(pageContainer).getByTestId('btn-open-manual')

    expect(manualButton).toBeDisabled()

    fireEvent.change(input, { target: { value: '99999999' } })

    expect(manualButton).toBeDisabled()
    await waitFor(() => expect((input as HTMLInputElement).value).toBe('99999999'))

    await waitFor(() => expect(fetchProductMock).toHaveBeenCalledWith('99999999'))
    expect(fetchProductMock).toHaveBeenCalledTimes(1)
    await waitFor(() => expect(screen.getByText(/Aucun produit trouvé/)).toBeInTheDocument())
    await waitFor(() => expect(manualButton).toBeEnabled())
  })

  it("permet d'ajouter un produit inconnu via l'ajout manuel", async () => {
    const notFoundError: HttpError = {
      name: 'Error',
      message: 'HTTP 404',
      status: 404,
      url: '/api/products/99999999',
    }

    fetchProductMock.mockRejectedValueOnce(notFoundError)

    renderInventoryRoutes('/inventory/session', {
      initialize: (inventory) => {
        inventory.setSelectedUser(shopUsers[0])
        inventory.setCountType(CountType.Count1)
        inventory.setLocation({ ...reserveLocation })
        inventory.clearSession()
      },
    })

    const sessionPages = await screen.findAllByTestId('page-session')
    const activeSessionPage = sessionPages[sessionPages.length - 1]
    const input = within(activeSessionPage).getByLabelText('Scanner (douchette ou saisie)')
    const manualButton = within(activeSessionPage).getByTestId('btn-open-manual')

    expect(manualButton).toBeDisabled()

    fireEvent.change(input, { target: { value: '99999999\n' } })

    await waitFor(() => expect(fetchProductMock).toHaveBeenCalledWith('99999999'))
    await waitFor(() => expect(manualButton).toBeEnabled())

    fireEvent.click(manualButton)

    await within(activeSessionPage).findByText('Produit inconnu EAN 99999999')
    await waitFor(() => expect(startInventoryRunMock).toHaveBeenCalledTimes(1))
    expect(startInventoryRunMock).toHaveBeenCalledWith(
      reserveLocation.id,
      expect.objectContaining({
        countType: 1,
        ownerUserId: shopUsers[0].id,
        shopId: testShop.id,
      }),
    )
  })

  it("enregistre l'utilisateur sélectionné dans la session", async () => {
    renderInventoryRoutes('/inventory/start')

    const userButton = await screen.findByRole('button', { name: shopUsers[0].displayName })
    fireEvent.click(userButton)

    const storageKey = `${SELECTED_USER_STORAGE_PREFIX}.${testShop.id}`
    await waitFor(() =>
      expect(sessionStorage.getItem(storageKey)).toBe(
        JSON.stringify({ userId: shopUsers[0].id }),
      ),
    )
  })

  it("restaure l'utilisateur sélectionné depuis la session", async () => {
    const storageKey = `${SELECTED_USER_STORAGE_PREFIX}.${testShop.id}`
    sessionStorage.setItem(storageKey, JSON.stringify({ userId: shopUsers[0].id }))

    renderInventoryRoutes('/inventory/start')

    const userSummaries = await screen.findAllByText((content) =>
      content.replace(/\s+/g, ' ').includes(`Utilisateur : ${shopUsers[0].displayName}`),
    )
    expect(userSummaries.length).toBeGreaterThan(0)
  })

  it('démarre un run serveur lors du premier scan', async () => {
    renderInventoryRoutes('/inventory/session', {
      initialize: (inventory) => {
        inventory.setSelectedUser(shopUsers[0])
        inventory.setCountType(CountType.Count1)
        inventory.setLocation({ ...reserveLocation })
        inventory.clearSession()
      },
    })

    const sessionPages = await screen.findAllByTestId('page-session')
    const activeSessionPage = sessionPages[sessionPages.length - 1]
    const input = within(activeSessionPage).getByLabelText('Scanner (douchette ou saisie)')

    fireEvent.change(input, { target: { value: '123456' } })

    await waitFor(() => expect(fetchProductMock).toHaveBeenCalledWith('123456'))
    await within(activeSessionPage).findByText('Popcorn caramel')

    await waitFor(() => expect(startInventoryRunMock).toHaveBeenCalledTimes(1))
    const calls = startInventoryRunMock.mock.calls
    const lastCall = calls[calls.length - 1]
    expect(lastCall).toBeDefined()
    const [locationId, payload] = lastCall!
    expect(locationId).toBe(reserveLocation.id)
    expect(payload).toMatchObject({ countType: 1, ownerUserId: shopUsers[0].id, shopId: testShop.id })
  })

  it("n'effectue pas de recherche produit sans boutique sélectionnée", async () => {
    localStorage.removeItem('cb.shop')

    renderInventoryRoutes('/inventory/session', {
      initialize: (inventory) => {
        inventory.setSelectedUser(shopUsers[0])
        inventory.setCountType(CountType.Count1)
        inventory.setLocation({ ...reserveLocation })
        inventory.clearSession()
      },
    })

    const sessionPages = await screen.findAllByTestId('page-session')
    const activeSessionPage = sessionPages[sessionPages.length - 1]
    const input = within(activeSessionPage).getByLabelText('Scanner (douchette ou saisie)')

    fireEvent.change(input, { target: { value: '123456' } })
    fireEvent.keyDown(input, { key: 'Enter' })

    await waitFor(() =>
      expect(
        within(activeSessionPage).getByText('Sélectionnez une boutique valide avant de scanner un produit.'),
      ).toBeInTheDocument(),
    )

    expect(fetchProductMock).not.toHaveBeenCalled()
  })

  it("ne libère pas le run immédiatement après l'avoir démarré", async () => {
    renderInventoryRoutes('/inventory/session', {
      initialize: (inventory) => {
        inventory.setSelectedUser(shopUsers[0])
        inventory.setCountType(CountType.Count1)
        inventory.setLocation({ ...reserveLocation })
        inventory.clearSession()
      },
    })

    const sessionPages = await screen.findAllByTestId('page-session')
    const activeSessionPage = sessionPages[sessionPages.length - 1]
    const input = within(activeSessionPage).getByLabelText('Scanner (douchette ou saisie)')

    fireEvent.change(input, { target: { value: '123456' } })

    await within(activeSessionPage).findByText('Popcorn caramel')
    await waitFor(() => expect(startInventoryRunMock).toHaveBeenCalledTimes(1))

    await waitFor(() => {
      expect(releaseInventoryRunMock).not.toHaveBeenCalled()
    })
  })

  it('libère automatiquement la zone lorsque tous les articles sont supprimés', async () => {
    renderInventoryRoutes('/inventory/session', {
      initialize: (inventory) => {
        inventory.setSelectedUser(shopUsers[0])
        inventory.setCountType(CountType.Count1)
        inventory.setLocation({ ...reserveLocation })
        inventory.setSessionId('run-lock-1')
        inventory.addOrIncrementItem({ ean: '1234567890123', name: 'Popcorn caramel' })
      },
    })

    const sessionPages = await screen.findAllByTestId('page-session')
    const activeSessionPage = sessionPages[sessionPages.length - 1]

    const initialItem = await within(activeSessionPage).findByTestId('scanned-item')
    expect(initialItem).toBeInTheDocument()

    const removeButton = within(initialItem).getByRole('button', { name: 'Retirer' })
    fireEvent.click(removeButton)

    await waitFor(() => expect(within(activeSessionPage).queryByTestId('scanned-item')).not.toBeInTheDocument())
    await waitFor(() =>
      expect(releaseInventoryRunMock).toHaveBeenCalledWith(
        reserveLocation.id,
        'run-lock-1',
        shopUsers[0].id,
      ),
    )

    expect(within(activeSessionPage).queryByText(/Session existante/)).not.toBeInTheDocument()
  })

  it("conserve l'ordre d'insertion des articles lors des ajustements de quantité", async () => {
    renderInventoryRoutes('/inventory/session', {
      initialize: (inventory) => {
        inventory.setSelectedUser(shopUsers[0])
        inventory.setCountType(CountType.Count1)
        inventory.setLocation({ ...reserveLocation })
        inventory.clearSession()
        inventory.addOrIncrementItem({ ean: '001', name: 'Produit A' })
        inventory.addOrIncrementItem({ ean: '0000', name: 'Produit B' })
      },
    })

    const sessionPages = await screen.findAllByTestId('page-session')
    const activeSessionPage = sessionPages[sessionPages.length - 1]

    const readRenderedEans = () =>
      within(activeSessionPage)
        .queryAllByTestId('scanned-item')
        .map((element) => element.getAttribute('data-ean') ?? '')

    await waitFor(() => expect(readRenderedEans()).toEqual(['001', '0000']))

    const initialRows = within(activeSessionPage).getAllByTestId('scanned-item')
    expect(initialRows).toHaveLength(2)
    const secondRow = initialRows[1]
    const incrementButton = within(secondRow).getByRole('button', { name: 'Ajouter' })

    fireEvent.click(incrementButton)

    await waitFor(() => expect(within(secondRow).getByText('2')).toBeInTheDocument())
    await waitFor(() => expect(readRenderedEans()).toEqual(['001', '0000']))

    const updatedRows = within(activeSessionPage).getAllByTestId('scanned-item')
    const updatedSecondRow = updatedRows[1]
    const decrementButton = within(updatedSecondRow).getByRole('button', { name: 'Retirer' })

    fireEvent.click(decrementButton)

    await waitFor(() => expect(within(updatedSecondRow).getByText('1')).toBeInTheDocument())
    await waitFor(() => expect(readRenderedEans()).toEqual(['001', '0000']))
  })

  it('envoie le comptage finalisé lorsque le bouton est actionné', async () => {
    renderInventoryRoutes('/inventory/session', {
      initialize: (inventory) => {
        inventory.setSelectedUser(shopUsers[0])
        inventory.setCountType(CountType.Count2)
        inventory.setLocation({ ...reserveLocation })
        inventory.clearSession()
      },
    })

    const sessionPages = await screen.findAllByTestId('page-session')
    const activeSessionPage = sessionPages[sessionPages.length - 1]
    const input = within(activeSessionPage).getByLabelText('Scanner (douchette ou saisie)')
    fireEvent.change(input, { target: { value: '123' } })

    await waitFor(() => expect(fetchProductMock).toHaveBeenCalledWith('123'))
    await within(activeSessionPage).findByText('Popcorn caramel')
    await waitFor(() => expect(startInventoryRunMock).toHaveBeenCalledTimes(1))
    const finishButton = await within(activeSessionPage).findByTestId('btn-complete-run')
    expect(finishButton).toBeInTheDocument()

    fireEvent.click(finishButton)

    await waitFor(() => expect(completeInventoryRunMock).toHaveBeenCalledTimes(1))
    expect(completeInventoryRunMock.mock.calls.length).toBeGreaterThan(0)
    const [locationId, payload] = completeInventoryRunMock.mock.calls.at(-1)!
    expect(payload).toBeTruthy()
    expect(locationId).toBeTruthy()
    expect(payload).toMatchObject({
      runId: 'run-lock-1',
      countType: CountType.Count2,
      ownerUserId: shopUsers[0].id,
    })
    expect(payload.items).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          ean: '123',
          isManual: false,
        }),
      ]),
    )
  })

})
