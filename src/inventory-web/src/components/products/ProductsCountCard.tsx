import React from 'react'

type Props = {
  shopId: string
  onOpen?: () => void
}

export function ProductsCountCard({ shopId, onOpen }: Props) {
  const [state, setState] = React.useState<{ count: number; hasCatalog: boolean } | null>(null)
  const [loading, setLoading] = React.useState(true)

  React.useEffect(() => {
    let aborted = false
    ;(async () => {
      try {
        const res = await fetch(`/api/shops/${shopId}/products/count`)
        if (!res.ok) throw new Error('count failed')
        const json = await res.json()
        if (!aborted) setState(json)
      } catch {
        if (!aborted) setState({ count: 0, hasCatalog: false })
      } finally {
        if (!aborted) setLoading(false)
      }
    })()
    return () => {
      aborted = true
    }
  }, [shopId])

  const count = state?.count ?? 0
  const hasData = count > 0
  const common = 'w-full rounded-lg border bg-white shadow-sm p-4 text-left'

  if (!hasData) {
    return (
      <div className={`${common} cursor-default`}>
        {loading ? (
          <div className="h-5 w-24 animate-pulse rounded bg-gray-200" />
        ) : (
          <p className="text-sm text-gray-500">Aucun produit chargé pour cette boutique</p>
        )}
      </div>
    )
  }

  const click = typeof onOpen === 'function' ? onOpen : undefined

  return (
    <button
      type="button"
      onClick={click}
      className={`${common} hover:bg-gray-50 focus-visible:ring-2 focus-visible:ring-sky-500 focus:outline-none ring-offset-2`}
      aria-label="Ouvrir le catalogue produits"
    >
      {loading ? (
        <div className="h-5 w-24 animate-pulse rounded bg-gray-200" />
      ) : (
        <div className="flex items-baseline gap-2">
          <span className="text-lg font-semibold text-gray-900">{count}</span>
          <span className="text-sm text-gray-600">produits</span>
        </div>
      )}
    </button>
  )
}
