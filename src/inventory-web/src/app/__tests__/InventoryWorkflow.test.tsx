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

const { fetchLocationsMock, fetchProductMock, reserveLocation } = vi.hoisted(() => {
  const reserveLocation = {
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
        status: 'not_started' as const,
        runId: null,
        operatorDisplayName: null,
        startedAtUtc: null,
        completedAtUtc: null,
      },
      {
        countType: 2,
        status: 'not_started' as const,
        runId: null,
        operatorDisplayName: null,
        startedAtUtc: null,
        completedAtUtc: null,
      },
    ],
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
    countStatuses: [
      {
        countType: 1,
        status: 'in_progress' as const,
        runId: 'run-1',
        operatorDisplayName: 'paul.dupont',
        startedAtUtc: new Date().toISOString(),
        completedAtUtc: null,
      },
      {
        countType: 2,
        status: 'not_started' as const,
        runId: null,
        operatorDisplayName: null,
        startedAtUtc: null,
        completedAtUtc: null,
      },
    ],
  }
  return {
    fetchLocationsMock: vi.fn(() =>
      Promise.resolve([
        { ...reserveLocation },
        { ...busyLocation },
      ]),
    ),
    fetchProductMock: vi.fn(() => Promise.resolve({ ean: '123', name: 'Popcorn caramel' })),
    reserveLocation,
  }
})

vi.mock('../api/inventoryApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/inventoryApi')>()
  return {
    ...actual,
    fetchLocations: fetchLocationsMock,
    fetchProductByEan: fetchProductMock,
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

describe('Workflow d\'inventaire', () => {
  beforeEach(() => {
    fetchLocationsMock.mockClear()
    fetchProductMock.mockClear()
  })

  it('permet de sélectionner utilisateur, zone et type en respectant les statuts', async () => {
    renderInventoryRoutes('/inventory/start')

    fireEvent.click(screen.getByRole('button', { name: 'Amélie' }))

    const locationPages = await screen.findAllByTestId('page-location')
    expect(locationPages).not.toHaveLength(0)

    await waitFor(() => expect(fetchLocationsMock).toHaveBeenCalledTimes(1))
    expect(fetchLocationsMock.mock.calls[0]).toHaveLength(0)

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

  it('autorise la reprise de son propre comptage', async () => {
    const selfRunLocation = {
      ...reserveLocation,
      id: 'zone-3',
      code: 'SAL2',
      label: 'Salle 2',
      isBusy: true,
      busyBy: 'Amélie',
      activeRunId: 'run-self',
      activeCountType: 1,
      activeStartedAtUtc: new Date().toISOString(),
      countStatuses: [
        {
          countType: 1,
          status: 'in_progress' as const,
          runId: 'run-self',
          operatorDisplayName: 'Amélie',
          startedAtUtc: new Date().toISOString(),
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

    fireEvent.click(screen.getByRole('button', { name: 'Amélie' }))

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

})
