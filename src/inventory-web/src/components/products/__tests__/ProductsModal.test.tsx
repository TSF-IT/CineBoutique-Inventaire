import { render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { ProductsModal } from '../ProductsModal'

describe('ProductsModal', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('charge les produits puis affiche la table', async () => {
    const shopId = '00000000-0000-0000-0000-000000000001'
    const response = {
      items: [
        {
          id: 'p1',
          sku: 'SKU-1',
          name: 'Produit 1',
          ean: '1234567890123',
          description: 'Article test',
          codeDigits: '99'
        }
      ],
      page: 1,
      pageSize: 50,
      total: 1,
      totalPages: 1,
      sortBy: 'sku',
      sortDir: 'asc' as const,
      q: ''
    }

    let resolveJson: (value: typeof response) => void = () => {}
    const jsonPromise = new Promise<typeof response>((resolve) => {
      resolveJson = resolve
    })

    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: vi.fn(() => jsonPromise)
    })

    vi.stubGlobal('fetch', fetchMock)

    render(<ProductsModal open={true} onClose={() => {}} shopId={shopId} />)

    expect(await screen.findByText('Chargement…')).toBeInTheDocument()

    resolveJson(response)

    await waitFor(() => expect(screen.getByText('Produit 1')).toBeInTheDocument())
    await waitFor(() => expect(screen.queryByText('Chargement…')).not.toBeInTheDocument())

    expect(fetchMock).toHaveBeenCalledWith(
      `/api/shops/${shopId}/products?page=1&pageSize=50&q=&sortBy=sku&sortDir=asc`
    )

    const rows = within(screen.getByRole('table')).getAllByRole('row')
    expect(rows).toHaveLength(2)
  })
})
