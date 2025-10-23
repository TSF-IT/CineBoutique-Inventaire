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
    'flex w-full min-h-[192px] flex-col gap-3 rounded-xl border p-5 text-left shadow-elev-1 transition'
  const defaultToneClasses =
    'border-product-200 bg-product-50/80 dark:border-product-600/35 dark:bg-product-600/15'

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

  if (!hasData) {
    return (
      <div className={clsx(baseCardClasses, defaultToneClasses, 'cursor-default', className)}>
        {loading ? (
          <>
            <div className="h-4 w-32 animate-pulse rounded bg-product-200/50" />
            <div className="h-10 w-40 animate-pulse rounded bg-product-200/60" />
          </>
        ) : (
          <>
            <p className="text-sm uppercase text-product-600 dark:text-product-200">Catalogue produits</p>
            <p className="mt-2 text-lg font-semibold text-product-700 dark:text-product-200">
              Aucun produit dans le catalogue
            </p>
          </>
        )}
      </div>
    )
  }

  return (
    <button
      type="button"
      onClick={handleClick}
      className={clsx(
        baseCardClasses,
        defaultToneClasses,
        'cursor-pointer hover:shadow-elev-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-product-600 focus-visible:ring-offset-2',
        className,
      )}
      aria-label="Ouvrir le catalogue produits"
      {...rest}
    >
      {loading ? (
        <>
          <div className="h-4 w-32 animate-pulse rounded bg-product-200/50" />
          <div className="h-10 w-24 animate-pulse rounded bg-product-200/60" />
        </>
      ) : (
        <>
          <div className="flex items-center justify-between gap-2">
            <p className="text-sm uppercase text-product-600 dark:text-product-200">Catalogue produits</p>
            {state?.hasCatalog && (
              <span className="inline-flex items-center rounded-full bg-product-200 px-2 py-0.5 text-xs font-semibold text-product-700 dark:bg-product-600/30 dark:text-white">
                Catalogue importé
              </span>
            )}
          </div>
          <p className="mt-2 text-4xl font-semibold text-product-700 dark:text-white">{count}</p>
          <p className="text-xs font-semibold uppercase tracking-wide text-product-600/80 dark:text-product-200/80">
            Produits référencés
          </p>
          <p className="mt-1 text-xs text-product-600/70 dark:text-product-200/70">Touchez pour voir le catalogue</p>
        </>
      )}
    </button>
  )
}
