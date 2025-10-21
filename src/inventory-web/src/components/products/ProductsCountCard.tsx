import React from 'react'
import clsx from 'clsx'

type Props = {
  shopId: string
  onClick: () => void
}

export function ProductsCountCard({ shopId, onClick }: Props) {
  const [state, setState] = React.useState<{ count: number; hasCatalog: boolean } | null>(null)
  const [loading, setLoading] = React.useState(true)

  React.useEffect(() => {
    let aborted = false
    (async () => {
      try {
        const res = await fetch(`/api/shops/${shopId}/products/count`)
        if (!res.ok) throw new Error('count failed')
        const json = await res.json()
        if (!aborted) setState(json)
      } finally {
        if (!aborted) setLoading(false)
      }
    })()
    return () => {
      aborted = true
    }
  }, [shopId])

  const hasProducts = !!state && state.count > 0
  const isInteractive = hasProducts || loading
  const cardClasses = clsx(
    'flex h-full w-full flex-col rounded-2xl border bg-white p-5 text-left text-gray-900 shadow-sm transition',
    isInteractive
      ? 'cursor-pointer border-slate-300 hover:border-sky-300 hover:shadow-md focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-sky-500 focus-visible:ring-offset-2'
      : 'cursor-default border-slate-200'
  )

  const content = (
    <div className="flex flex-col gap-4">
      <div className="flex items-center gap-3">
        <span className="inline-flex h-10 w-10 items-center justify-center rounded-full border border-sky-200 bg-sky-50">
          <span className="h-2 w-2 rounded-full bg-sky-500" aria-hidden="true" />
        </span>
        <p className="text-sm font-semibold uppercase tracking-wide text-sky-600">Produits</p>
      </div>
      {loading ? (
        <div className="flex flex-col gap-2">
          <div className="h-5 w-24 animate-pulse rounded bg-gray-200" />
          <div className="h-3 w-32 animate-pulse rounded bg-gray-100" />
        </div>
      ) : hasProducts ? (
        <div className="flex items-baseline gap-2">
          <span className="text-4xl font-semibold">{state.count}</span>
          <span className="text-sm text-gray-600">produits</span>
        </div>
      ) : (
        <p className="text-sm text-gray-600">Aucun produit chargé pour cette boutique</p>
      )}
    </div>
  )

  if (isInteractive) {
    return (
      <button type="button" onClick={onClick} className={cardClasses} aria-label="Ouvrir le catalogue produits">
        {content}
      </button>
    )
  }

  return <div className={cardClasses}>{content}</div>
}
