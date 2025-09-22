import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { vi } from 'vitest'
import { ThemeProvider } from '../../theme/ThemeProvider'
import { HomePage } from '../pages/home/HomePage'

vi.mock('../api/inventoryApi', () => ({
  fetchInventorySummary: vi.fn(() =>
    Promise.resolve({ activeSessions: 3, openRuns: 1, lastActivityUtc: '2025-01-01T12:00:00Z' }),
  ),
}))

describe('HomePage', () => {
  it("affiche les indicateurs et le bouton d'accès", async () => {
    render(
      <ThemeProvider>
        <MemoryRouter>
          <HomePage />
        </MemoryRouter>
      </ThemeProvider>,
    )

    await waitFor(() => {
      expect(screen.getByText('Sessions actives')).toBeInTheDocument()
      expect(screen.getByText('3')).toBeInTheDocument()
      expect(screen.getByText('Runs ouverts')).toBeInTheDocument()
    })

    expect(screen.getByRole('button', { name: 'Débuter un inventaire' })).toBeInTheDocument()
  })
})
