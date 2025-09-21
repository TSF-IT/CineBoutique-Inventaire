import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { vi } from 'vitest'
import { HomePage } from '../pages/home/HomePage'

vi.mock('../api/inventoryApi', () => ({
  fetchInventorySummary: vi.fn(() => Promise.resolve({ activeCounts: 3, conflicts: 1 })),
}))

describe('HomePage', () => {
  it("affiche les indicateurs et le bouton d'accès", async () => {
    render(
      <MemoryRouter>
        <HomePage />
      </MemoryRouter>,
    )

    await waitFor(() => {
      expect(screen.getByText('3')).toBeInTheDocument()
      expect(screen.getByText('1')).toBeInTheDocument()
    })

    expect(screen.getByRole('button', { name: 'Débuter un inventaire' })).toBeInTheDocument()
  })
})
