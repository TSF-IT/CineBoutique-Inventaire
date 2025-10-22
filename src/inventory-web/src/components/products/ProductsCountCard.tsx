import clsx from 'clsx'
import React from 'react'

type Props = {
  shopId: string
  onOpen?: () => void
} & React.ComponentProps<'button'>

export function ProductsCountCard({ shopId, onOpen, onClick, className, ...rest }: Props) {
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
  const baseCardClasses =
    'flex w-full flex-col gap-3 rounded-xl border border-product-700/20 bg-product-50/70 p-5 text-left shadow-elev-1 transition dark:border-product-700/40 dark:bg-product-50/10 dark:text-product-200'

  if (!hasData) {
    return (
      <div className={clsx(baseCardClasses, 'cursor-default', className)}>
        {loading ? (
          <div className="h-5 w-24 animate-pulse rounded bg-product-200/60" />
        ) : (
          <p className="text-sm text-product-700 dark:text-product-200/80">
            Aucun produit chargé pour cette boutique
          </p>
        )}
      </div>
    )
  }

  const handleClick = React.useCallback(
    (event: React.MouseEvent<HTMLButtonElement>) => {
      if (typeof onClick === 'function') {
        onClick(event)
      }
      if (!event.defaultPrevented && typeof onOpen === 'function') {
        onOpen()
      }
    },
    [onClick, onOpen]
  )

  return (
    <button
      type="button"
      onClick={handleClick}
      className={clsx(
        baseCardClasses,
        'hover:shadow-elev-2 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-product-600/70',
        className,
      )}
      aria-label="Ouvrir le catalogue produits"
      {...rest}
    >
      {loading ? (
        <div className="h-5 w-24 animate-pulse rounded bg-product-200/60" />
      ) : (
        <>
          <div className="flex items-center justify-between">
            <p className="text-sm font-bold text-product-700 dark:text-product-200">Catalogue produits</p>
            {state?.hasCatalog && (
              <span className="inline-flex items-center rounded-full bg-product-200 px-2 py-0.5 text-xs font-semibold text-product-700 dark:bg-product-700/30 dark:text-product-200">
                Catalogue importé
              </span>
            )}
          </div>
          <div className="flex items-baseline gap-2 text-product-700 dark:text-product-200">
            <span className="text-3xl font-bold">{count}</span>
            <span className="text-sm font-bold uppercase tracking-wide">produits</span>
          </div>
          <span className="inline-flex w-fit items-center rounded-md bg-product-600 px-3 py-2 text-sm font-semibold text-white transition hover:brightness-110">
            Voir le catalogue
          </span>
        </>
      )}
    </button>
  )
}
