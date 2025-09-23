import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AppProviders } from '../providers/AppProviders'
import { InventoryLayout } from '../pages/inventory/InventoryLayout'
import { InventoryUserStep } from '../pages/inventory/InventoryUserStep'
import { InventoryCountTypeStep } from '../pages/inventory/InventoryCountTypeStep'
import { InventoryLocationStep } from '../pages/inventory/InventoryLocationStep'
import { InventorySessionPage } from '../pages/inventory/InventorySessionPage'
import type { HttpError } from '@/lib/api/http'

const createHttpError = (overrides?: Partial<HttpError>) =>
  Object.assign(new Error(overrides?.message ?? 'HTTP 500'), {
    status: overrides?.status ?? 500,
    url: overrides?.url ?? 'http://localhost:8080/api',
    body: overrides?.body,
    problem: overrides?.problem,
  })

const { fetchLocationsMock, fetchProductMock, restartInventoryRunMock } = vi.hoisted(() => ({
  fetchLocationsMock: vi.fn(() =>
    Promise.resolve([
      {
        id: 'zone-1',
        code: 'RES',
        label: 'Réserve',
        isBusy: false,
        busyBy: null,
        activeRunId: null,
        activeCountType: null,
        activeStartedAtUtc: null,
      },
      {
        id: 'zone-2',
        code: 'SAL1',
        label: 'Salle 1',
        isBusy: true,
        busyBy: 'paul.dupont',
        activeCountType: 1,
        activeRunId: 'run-1',
        activeStartedAtUtc: new Date().toISOString(),
      },
    ]),
  ),
  fetchProductMock: vi.fn(() => Promise.resolve({ ean: '123', name: 'Popcorn caramel' })),
  restartInventoryRunMock: vi.fn(() => Promise.resolve()),
}))

vi.mock('../api/inventoryApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/inventoryApi')>()
  return {
    ...actual,
    fetchLocations: fetchLocationsMock,
    fetchProductByEan: fetchProductMock,
    restartInventoryRun: restartInventoryRunMock,
  }
})

const renderInventoryRoutes = (initialEntry: string) =>
  render(
    <AppProviders>
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
    </AppProviders>,
  )

describe('Workflow d\'inventaire', () => {
  beforeEach(() => {
    fetchLocationsMock.mockClear()
    fetchProductMock.mockClear()
    restartInventoryRunMock.mockClear()
  })

  it('permet de sélectionner utilisateur, type et zone', async () => {
    renderInventoryRoutes('/inventory/start')

    fireEvent.click(screen.getByRole('button', { name: 'Amélie' }))

    await waitFor(() => expect(screen.getByText('Quel type de comptage ?')).toBeInTheDocument())

    fireEvent.click(screen.getByRole('button', { name: /Comptage n°1/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Sélectionner la zone' }))

    await waitFor(() => expect(fetchLocationsMock).toHaveBeenCalledWith({ countType: 1 }))

    fireEvent.click(screen.getByRole('button', { name: /Zone Salle 1 occupée/ }))

    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Reprendre le comptage en cours' })).toBeInTheDocument(),
    )

    fireEvent.click(screen.getByRole('button', { name: 'Fermer' }))

    fireEvent.click(screen.getByRole('button', { name: /Zone Réserve libre/ }))

    await waitFor(() => expect(screen.getByText(/Session de comptage/)).toBeInTheDocument())
    await waitFor(() => expect(screen.getAllByText(/Réserve/).length).toBeGreaterThan(0))
  })

  it('ajoute un produit via saisie manuelle simulant une douchette', async () => {
    renderInventoryRoutes('/inventory/start')

    fireEvent.click(screen.getByRole('button', { name: 'Amélie' }))
    await waitFor(() => expect(screen.getByText('Quel type de comptage ?')).toBeInTheDocument())

    fireEvent.click(screen.getByRole('button', { name: /Comptage n°1/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Sélectionner la zone' }))
    await waitFor(() => expect(fetchLocationsMock).toHaveBeenCalledWith({ countType: 1 }))
    fireEvent.click(screen.getByRole('button', { name: /Zone Réserve libre/ }))

    const input = await screen.findByLabelText('Scanner (douchette ou saisie)')
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
    await waitFor(() => expect(screen.getByText('Quel type de comptage ?')).toBeInTheDocument())

    fireEvent.click(screen.getByRole('button', { name: /Comptage n°1/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Sélectionner la zone' }))

    await waitFor(() => expect(fetchLocationsMock).toHaveBeenCalledWith({ countType: 1 }))

    fireEvent.click(screen.getByRole('button', { name: /Zone Salle 1 occupée/ }))
    const restartButton = await screen.findByRole('button', { name: 'Redémarrer un nouveau comptage' })

    fireEvent.click(restartButton)

    await waitFor(() => expect(restartInventoryRunMock).toHaveBeenCalledWith('zone-2', 1))
    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Redémarrage impossible')
  })
})
