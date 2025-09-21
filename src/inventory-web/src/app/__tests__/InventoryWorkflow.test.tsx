import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { vi } from 'vitest'
import { AppProviders } from '../providers/AppProviders'
import { InventoryLayout } from '../pages/inventory/InventoryLayout'
import { InventoryUserStep } from '../pages/inventory/InventoryUserStep'
import { InventoryCountTypeStep } from '../pages/inventory/InventoryCountTypeStep'
import { InventoryLocationStep } from '../pages/inventory/InventoryLocationStep'
import { InventoryConfirmStep } from '../pages/inventory/InventoryConfirmStep'
import { InventorySessionPage } from '../pages/inventory/InventorySessionPage'

const { fetchLocationsMock, verifyInventoryMock, fetchProductMock } = vi.hoisted(() => ({
  fetchLocationsMock: vi.fn(() =>
    Promise.resolve([
      { id: 'zone-1', code: 'RES', label: 'Réserve', description: 'Arrière boutique' },
      { id: 'zone-2', code: 'SAL1', label: 'Salle 1' },
    ]),
  ),
  verifyInventoryMock: vi.fn(() => Promise.resolve({ hasActive: false })),
  fetchProductMock: vi.fn(() => Promise.resolve({ ean: '123', name: 'Popcorn caramel' })),
}))

vi.mock('../api/inventoryApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/inventoryApi')>()
  return {
    ...actual,
    fetchLocations: fetchLocationsMock,
    verifyInventoryInProgress: verifyInventoryMock,
    fetchProductByEan: fetchProductMock,
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
            <Route path="confirm" element={<InventoryConfirmStep />} />
            <Route path="session" element={<InventorySessionPage />} />
          </Route>
        </Routes>
      </MemoryRouter>
    </AppProviders>,
  )

describe('Workflow d\'inventaire', () => {
  beforeEach(() => {
    fetchLocationsMock.mockClear()
    verifyInventoryMock.mockClear()
    fetchProductMock.mockClear()
  })

  it('permet de sélectionner utilisateur, type et zone', async () => {
    renderInventoryRoutes('/inventory/start')

    fireEvent.click(screen.getByRole('button', { name: 'Amélie' }))

    await waitFor(() => expect(screen.getByText('Quel type de comptage ?')).toBeInTheDocument())

    fireEvent.click(screen.getByRole('button', { name: /1 comptage/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Sélectionner la zone' }))

    await waitFor(() => expect(fetchLocationsMock).toHaveBeenCalled())
    fireEvent.click(screen.getByRole('button', { name: /Réserve/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Vérifier la disponibilité' }))

    await waitFor(() => expect(verifyInventoryMock).toHaveBeenCalled())
    await waitFor(() =>
      expect(screen.getByText('Zone disponible')).toBeInTheDocument(),
    )
  })

  it('ajoute un produit via saisie manuelle simulant une douchette', async () => {
    renderInventoryRoutes('/inventory/start')

    fireEvent.click(screen.getByRole('button', { name: 'Amélie' }))
    await waitFor(() => expect(screen.getByText('Quel type de comptage ?')).toBeInTheDocument())

    fireEvent.click(screen.getByRole('button', { name: /1 comptage/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Sélectionner la zone' }))
    await waitFor(() => expect(fetchLocationsMock).toHaveBeenCalled())
    fireEvent.click(screen.getByRole('button', { name: /Réserve/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Vérifier la disponibilité' }))
    await waitFor(() => expect(verifyInventoryMock).toHaveBeenCalled())
    fireEvent.click(screen.getByRole('button', { name: 'Démarrer le comptage' }))

    const input = await screen.findByLabelText('Scanner (douchette ou saisie)')
    fireEvent.change(input, { target: { value: '123' } })
    fireEvent.keyDown(input, { key: 'Enter', code: 'Enter' })

    await waitFor(() => expect(fetchProductMock).toHaveBeenCalledWith('123'))
    await waitFor(() => expect(screen.getByText('Popcorn caramel')).toBeInTheDocument())
  })
})
