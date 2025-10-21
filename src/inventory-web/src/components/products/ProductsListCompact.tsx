import React from 'react'

type Item = { id: string; sku: string; name: string; ean?: string | null }

type Props = {
  shopId: string
  onPick: (item: { sku: string; name: string; ean?: string | null }) => void
}

export function ProductsListCompact({ shopId, onPick }: Props) {
  const [q, setQ] = React.useState('')
  const [page, setPage] = React.useState(1)
  const [data, setData] = React.useState<{ items: Item[]; page: number; totalPages: number } | null>(null)

  React.useEffect(() => {
    let abort = false
    const params = new URLSearchParams({
      page: String(page),
      pageSize: String(20),
      q: q.trim(),
      sortBy: 'sku',
      sortDir: 'asc',
    })
    ;(async () => {
      const res = await fetch(`/api/shops/${shopId}/products?` + params.toString())
      if (!res.ok) return
      const json = await res.json()
      if (!abort) setData({ items: json.items, page: json.page, totalPages: json.totalPages })
    })()
    return () => {
      abort = true
    }
  }, [shopId, page, q])

  return (
    <div className="space-y-2">
      <input
        value={q}
        onChange={(e) => {
          setPage(1)
          setQ(e.target.value)
        }}
        placeholder="Trouver un produit…"
        className="w-full rounded border px-3 py-2 text-sm"
      />
      <ul className="divide-y">
        {data?.items.map((p) => (
          <li key={p.id}>
            <button
              type="button"
              onClick={async () => {
                const ok = window.confirm(`Ajouter « ${p.name} » ?`)
                if (ok) onPick({ sku: p.sku, name: p.name, ean: p.ean ?? undefined })
              }}
              className="flex w-full items-center justify-between px-2 py-2 text-left hover:bg-gray-50"
            >
              <div className="truncate">
                <div className="truncate text-sm font-medium">{p.name}</div>
                <div className="truncate text-xs text-gray-500">
                  {p.sku}
                  {p.ean ? ` • ${p.ean}` : ''}
                </div>
              </div>
              <span className="text-xs text-gray-400">+</span>
            </button>
          </li>
        ))}
      </ul>

      <div className="flex justify-between pt-2 text-sm">
        <button
          disabled={!data || data.page <= 1}
          onClick={() => setPage((p) => Math.max(1, p - 1))}
          className="rounded border px-2 py-1 disabled:opacity-50"
        >
          Préc.
        </button>
        <button
          disabled={!data || data.page >= data.totalPages}
          onClick={() => setPage((p) => p + 1)}
          className="rounded border px-2 py-1 disabled:opacity-50"
        >
          Suiv.
        </button>
      </div>
    </div>
  )
}
