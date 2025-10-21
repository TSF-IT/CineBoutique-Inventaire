import { render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { ProductsCountCard } from '../ProductsCountCard'

describe('ProductsCountCard', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('se rend et affiche le total récupéré', async () => {
    const shopId = '00000000-0000-0000-0000-000000000001'
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: vi.fn().mockResolvedValue({ count: 42, hasCatalog: true })
    })

    vi.stubGlobal('fetch', fetchMock)

    render(<ProductsCountCard shopId={shopId} onClick={() => {}} />)

    await waitFor(() => expect(screen.getByText('42')).toBeInTheDocument())
    expect(screen.getByRole('button', { name: 'Ouvrir le catalogue produits' })).toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledWith(`/api/shops/${shopId}/products/count`)
  })

  it('affiche un message et n’est pas cliquable quand aucun produit n’est disponible', async () => {
    const shopId = '00000000-0000-0000-0000-000000000002'
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: vi.fn().mockResolvedValue({ count: 0, hasCatalog: false })
    })

    vi.stubGlobal('fetch', fetchMock)

    render(<ProductsCountCard shopId={shopId} onClick={() => {}} />)

    await waitFor(() =>
      expect(screen.getByText('Aucun produit chargé pour cette boutique')).toBeInTheDocument()
    )

    expect(screen.queryByRole('button', { name: 'Ouvrir le catalogue produits' })).not.toBeInTheDocument()
  })
})
