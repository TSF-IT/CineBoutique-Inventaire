import { type ChangeEvent, useCallback, useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { fetchShops } from '@/api/shops'
import type { Shop } from '@/types/shop'
import { useShop } from '@/state/ShopContext'
import { Page } from '@/app/components/Page'
import { LoadingIndicator } from '@/app/components/LoadingIndicator'
import { Button } from '@/app/components/ui/Button'

const DEFAULT_ERROR_MESSAGE = "Impossible de charger les boutiques."

type LoadingState = 'idle' | 'loading' | 'error'

export const SelectShopPage = () => {
  const { shop, setShop } = useShop()
  const navigate = useNavigate()
  const [shops, setShops] = useState<Shop[]>([])
  const [status, setStatus] = useState<LoadingState>('loading')
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [retryCount, setRetryCount] = useState(0)
  const [selectedId, setSelectedId] = useState(() => shop?.id ?? '')

  useEffect(() => {
    let isMounted = true
    const controller = new AbortController()

    setStatus('loading')
    setErrorMessage(null)

    fetchShops(controller.signal)
      .then((data) => {
        if (!isMounted) {
          return
        }
        setShops(data)
        setStatus('idle')
      })
      .catch((error) => {
        if (!isMounted) {
          return
        }
        console.error('[select-shop] échec du chargement des boutiques', error)
        setShops([])
        setStatus('error')
        setErrorMessage(error instanceof Error ? error.message : DEFAULT_ERROR_MESSAGE)
      })

    return () => {
      isMounted = false
      controller.abort()
    }
  }, [retryCount])

  useEffect(() => {
    setSelectedId((current) => {
      if (shops.length === 0) {
        return shop?.id ?? ''
      }

      if (current && shops.some((item) => item.id === current)) {
        return current
      }

      if (shop && shops.some((item) => item.id === shop.id)) {
        return shop.id
      }

      if (!shop && shops.length === 1) {
        return shops[0].id
      }

      return ''
    })
  }, [shop, shops])

  const handleRetry = useCallback(() => {
    setRetryCount((count) => count + 1)
  }, [])

  const onSelectChange = useCallback((event: ChangeEvent<HTMLSelectElement>) => {
    setSelectedId(event.target.value)
  }, [])

  const selectedShop = useMemo(() => shops.find((item) => item.id === selectedId) ?? null, [selectedId, shops])

  const onContinue = useCallback(() => {
    if (!selectedShop) {
      return
    }
    setShop(selectedShop)
    navigate('/', { replace: true })
  }, [navigate, selectedShop, setShop])

  return (
    <Page className="justify-between px-4 py-6 sm:px-6">
      <main className="flex flex-1 flex-col justify-center gap-8">
        <div className="space-y-2">
          <h1 className="text-2xl font-semibold text-slate-900 dark:text-slate-100">Choisir une boutique</h1>
          <p className="text-sm text-slate-600 dark:text-slate-300">
            Sélectionnez la boutique sur laquelle vous travaillez pour personnaliser votre expérience.
          </p>
        </div>

        {status === 'loading' && (
          <LoadingIndicator label="Chargement des boutiques…" />
        )}

        {status === 'error' && (
          <div className="rounded-2xl border border-red-200 bg-red-50 p-4 text-sm text-red-800 dark:border-red-900/60 dark:bg-red-950/40 dark:text-red-200">
            <p className="font-semibold">{errorMessage ?? DEFAULT_ERROR_MESSAGE}</p>
            <p className="mt-1">Vérifiez votre connexion puis réessayez.</p>
            <Button className="mt-4" variant="secondary" onClick={handleRetry}>
              Réessayer
            </Button>
          </div>
        )}

        {status === 'idle' && (
          <form className="space-y-6" onSubmit={(event) => event.preventDefault()}>
            <label className="block text-sm font-medium text-slate-700 dark:text-slate-200" htmlFor="shop-select">
              Boutique
            </label>
            <div className="space-y-4">
              <select
                id="shop-select"
                className="w-full rounded-2xl border border-slate-300 bg-white px-4 py-3 text-base text-slate-900 shadow-sm focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-200 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-100"
                value={selectedId}
                onChange={onSelectChange}
                aria-describedby={shops.length === 0 ? 'shop-help' : undefined}
              >
                <option value="">Sélectionner…</option>
                {shops.map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.name}
                  </option>
                ))}
              </select>
              {shops.length === 0 && (
                <p id="shop-help" className="text-sm text-slate-500 dark:text-slate-300">
                  Aucune boutique n’est disponible pour le moment.
                </p>
              )}
            </div>
            <Button
              fullWidth
              disabled={!selectedShop}
              onClick={onContinue}
              aria-disabled={!selectedShop}
            >
              Continuer
            </Button>
          </form>
        )}
      </main>

      <p className="text-xs text-slate-500 dark:text-slate-400">
        Astuce : vous pourrez changer de boutique depuis l’accueil.
      </p>
    </Page>
  )
}

export default SelectShopPage
