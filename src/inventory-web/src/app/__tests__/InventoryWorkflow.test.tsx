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
import type { HttpError } from '@/lib/api/http'

const createHttpError = (overrides?: Partial<HttpError>) =>
  Object.assign(new Error(overrides?.message ?? 'HTTP 500'), {
    status: overrides?.status ?? 500,
    url: overrides?.url ?? 'http://localhost:8080/api',
    body: overrides?.body,
    problem: overrides?.problem,
  })

const {
  fetchLocationsMock,
  fetchProductMock,
  restartInventoryRunMock,
  reserveLocation,
  busyLocation,
} = vi.hoisted(() => {
  const reserveLocation = {
    id: 'zone-1',
    code: 'RES',
    label: 'Réserve',
    isBusy: false,
    busyBy: null,
    activeRunId: null,
    activeCountType: null,
    activeStartedAtUtc: null,
  }
  const busyLocation = {
    id: 'zone-2',
    code: 'SAL1',
    label: 'Salle 1',
    isBusy: true,
    busyBy: 'paul.dupont',
    activeCountType: 1,
    activeRunId: 'run-1',
    activeStartedAtUtc: new Date().toISOString(),
  }
  return {
    fetchLocationsMock: vi.fn(() =>
      Promise.resolve([
        { ...reserveLocation },
        { ...busyLocation },
      ]),
    ),
    fetchProductMock: vi.fn(() => Promise.resolve({ ean: '123', name: 'Popcorn caramel' })),
    restartInventoryRunMock: vi.fn(() => Promise.resolve()),
    reserveLocation,
    busyLocation,
  }
})

vi.mock('../api/inventoryApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/inventoryApi')>()
  return {
    ...actual,
    fetchLocations: fetchLocationsMock,
    fetchProductByEan: fetchProductMock,
    restartInventoryRun: restartInventoryRunMock,
  }
})

interface RenderInventoryOptions {
  initialize?: (inventory: ReturnType<typeof useInventory>) => void
}

const renderInventoryRoutes = (initialEntry: string, options?: RenderInventoryOptions) => {
  const initialize = options?.initialize

  const Bootstrapper = ({ children }: { children: ReactNode }) => {
    const inventory = useInventory()
    const initializedRef = useRef(false)
    const [ready, setReady] = useState(!initialize)

    useLayoutEffect(() => {
      if (initializedRef.current || !initialize) {
        return
      }
      initialize(inventory)
      initializedRef.current = true
      setReady(true)
    }, [initialize, inventory])

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
          <Route path="count-type" element={<InventoryCountTypeStep />} />
          <Route path="location" element={<InventoryLocationStep />} />
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

describe('Workflow d\'inventaire', () => {
  beforeEach(() => {
    fetchLocationsMock.mockClear()
    fetchProductMock.mockClear()
    restartInventoryRunMock.mockClear()
  })

  it('permet de sélectionner utilisateur, type et zone', async () => {
    renderInventoryRoutes('/inventory/start')

    fireEvent.click(screen.getByRole('button', { name: 'Amélie' }))

    await waitFor(() => expect(screen.getAllByTestId('page-count-type').length).toBeGreaterThan(0))

    const countTypePages = screen.getAllByTestId('page-count-type')
    const activeCountTypePage = countTypePages[countTypePages.length - 1]
    const countTypeOneButton = within(activeCountTypePage).getByTestId('btn-count-type-1')
    fireEvent.click(countTypeOneButton)
    const selectZoneButtons = screen.getAllByRole('button', { name: 'Sélectionner la zone' })
    fireEvent.click(selectZoneButtons[selectZoneButtons.length - 1])

    await waitFor(() => expect(fetchLocationsMock).toHaveBeenCalledWith({ countType: 1 }))

    const busyZoneCards = screen.getAllByTestId('zone-card-zone-2')
    const busyZoneCard = busyZoneCards[busyZoneCards.length - 1]
    fireEvent.click(within(busyZoneCard).getByRole('button', { name: 'Gérer la session en cours' }))

    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Reprendre le comptage en cours' })).toBeInTheDocument(),
    )

    fireEvent.click(screen.getByRole('button', { name: 'Fermer' }))

    const freeZoneCards = screen.getAllByTestId('zone-card-zone-1')
    const freeZoneCard = freeZoneCards[freeZoneCards.length - 1]
    fireEvent.click(within(freeZoneCard).getByTestId('btn-select-zone'))

    await waitFor(() =>
      expect(
        screen.getAllByText((content) => content.replace(/\s+/g, ' ').includes('Zone : Réserve')).length,
      ).toBeGreaterThan(0),
    )
    await waitFor(() =>
      expect(
        screen.getAllByText((content) => content.replace(/\s+/g, ' ').includes('Comptage : 1')).length,
      ).toBeGreaterThan(0),
    )
  })

  it('ajoute un produit via saisie manuelle simulant une douchette', async () => {
    renderInventoryRoutes('/inventory/session', {
      initialize: (inventory) => {
        inventory.setSelectedUser('Amélie')
        inventory.setCountType(CountType.Count1)
        inventory.setLocation({ ...reserveLocation })
        inventory.clearSession()
      },
    })

    const [input] = await screen.findAllByLabelText('Scanner (douchette ou saisie)')
    fireEvent.change(input, { target: { value: '123' } })
    fireEvent.keyDown(input, { key: 'Enter', code: 'Enter' })

    await waitFor(() => expect(fetchProductMock).toHaveBeenCalledWith('123'))
    await waitFor(() => expect(screen.getByText('Popcorn caramel')).toBeInTheDocument())
  })

  it("affiche la feuille d'actions et gère un redémarrage en erreur", async () => {
    restartInventoryRunMock.mockRejectedValueOnce(
      createHttpError({ message: 'HTTP 500', status: 500, body: 'Erreur serveur', url: 'http://localhost:8080/api' }),
    )

    renderInventoryRoutes('/inventory/start')

    fireEvent.click(screen.getByRole('button', { name: 'Amélie' }))
    await waitFor(() => expect(screen.getAllByTestId('page-count-type').length).toBeGreaterThan(0))

    const countTypePages = screen.getAllByTestId('page-count-type')
    const activeCountTypePage = countTypePages[countTypePages.length - 1]
    const countTypeOneButton = within(activeCountTypePage).getByTestId('btn-count-type-1')
    fireEvent.click(countTypeOneButton)
    const selectZoneButtons = screen.getAllByRole('button', { name: 'Sélectionner la zone' })
    fireEvent.click(selectZoneButtons[selectZoneButtons.length - 1])

    await waitFor(() => expect(fetchLocationsMock).toHaveBeenCalledWith({ countType: 1 }))

    const busyZoneCards = screen.getAllByTestId('zone-card-zone-2')
    const busyZoneCard = busyZoneCards[busyZoneCards.length - 1]
    fireEvent.click(within(busyZoneCard).getByRole('button', { name: 'Gérer la session en cours' }))
    const restartButton = await screen.findByRole('button', { name: 'Redémarrer un nouveau comptage' })

    fireEvent.click(restartButton)

    await waitFor(() => expect(restartInventoryRunMock).toHaveBeenCalledWith('zone-2', 1))
    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Redémarrage impossible')
  })
})
