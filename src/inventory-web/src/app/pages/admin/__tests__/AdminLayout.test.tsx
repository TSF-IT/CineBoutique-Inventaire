import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { AdminLayout } from '../AdminLayout'
import { ThemeProvider } from '@/theme/ThemeProvider'
import { ShopProvider } from '@/state/ShopContext'
import { SHOP_STORAGE_KEY } from '@/lib/shopStorage'

beforeEach(() => {
  window.localStorage.setItem(
    SHOP_STORAGE_KEY,
    JSON.stringify({ id: 'shop-test', name: 'Boutique test', kind: 'boutique' }),
  )
})

afterEach(() => {
  window.localStorage.clear()
})

describe('AdminLayout', () => {
  it("affiche un lien de retour vers l'accueil", () => {
    render(
      <ThemeProvider>
        <ShopProvider>
          <MemoryRouter initialEntries={[{ pathname: '/admin' }]}> 
            <Routes>
              <Route path="/admin" element={<AdminLayout />} />
            </Routes>
          </MemoryRouter>
        </ShopProvider>
      </ThemeProvider>
    )

    const homeLink = screen.getByTestId('btn-go-home')
    expect(homeLink).toBeInTheDocument()
    expect(homeLink).toHaveAttribute('href', '/')
  })
})
