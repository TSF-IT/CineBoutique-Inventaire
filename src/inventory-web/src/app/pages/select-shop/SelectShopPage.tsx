import { type ChangeEvent, useCallback, useEffect, useMemo, useRef, useState, useId } from 'react'
import { useNavigate } from 'react-router-dom'
import { fetchShops } from '@/api/shops'
import type { Shop } from '@/types/shop'
import { useShop } from '@/state/ShopContext'
import { Page } from '@/app/components/Page'
import { LoadingIndicator } from '@/app/components/LoadingIndicator'
import { Button } from '@/app/components/ui/Button'
import clsx from 'clsx'

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
  const [isRedirecting, setIsRedirecting] = useState(false)
  const labelId = useId()
  const cardsLabelId = useId()
  const selectRef = useRef<HTMLSelectElement | null>(null)
  const cardRefs = useRef<Array<HTMLButtonElement | null>>([])

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
        if (error instanceof DOMException && error.name === 'AbortError') {
          return
        }
        console.error('[select-shop] échec du chargement des boutiques', error)
        setShops([])
        setStatus('error')
        if (error instanceof TypeError) {
          setErrorMessage('Connexion impossible. Vérifiez votre réseau puis réessayez.')
          return
        }
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
  const useCardLayout = !isRedirecting && status === 'idle' && shops.length > 0 && shops.length <= 5
  const isLoadingShops = status === 'loading'
  const shouldShowError = status === 'error' && !isRedirecting
  const shouldShowForm = status === 'idle' && !isRedirecting

  useEffect(() => {
    if (!useCardLayout) {
      cardRefs.current = []
    }
  }, [useCardLayout])

  useEffect(() => {
    if (status !== 'idle' || isRedirecting) {
      return
    }

    if (useCardLayout) {
      const targetIndex = selectedId
        ? shops.findIndex((item) => item.id === selectedId)
        : 0
      const fallbackIndex = targetIndex >= 0 ? targetIndex : 0
      const target = cardRefs.current[fallbackIndex]
      target?.focus()
      return
    }

    selectRef.current?.focus()
  }, [isRedirecting, selectedId, shops, status, useCardLayout])

  const onContinue = useCallback(() => {
    if (!selectedShop) {
      return
    }
    setIsRedirecting(true)
    setShop(selectedShop)
    navigate('/', { replace: true })
  }, [navigate, selectedShop, setShop])

  const handleCardSelect = useCallback((id: string) => {
    setSelectedId(id)
  }, [])

  return (
    <Page className="justify-between px-4 py-6 sm:px-6">
      <main className="flex flex-1 flex-col justify-center gap-8">
        <div className="space-y-2">
          <h1 className="text-2xl font-semibold text-slate-900 dark:text-slate-100">Choisir une boutique</h1>
          <p className="text-sm text-slate-600 dark:text-slate-300">
            Sélectionnez la boutique sur laquelle vous travaillez pour personnaliser votre expérience.
          </p>
        </div>

        {isLoadingShops && (
          <LoadingIndicator label="Chargement des boutiques…" />
        )}

        {shouldShowError && (
          <div
            className="rounded-2xl border border-red-200 bg-red-50 p-4 text-sm text-red-800 dark:border-red-900/60 dark:bg-red-950/40 dark:text-red-200"
            role="alert"
          >
            <p className="font-semibold">{errorMessage ?? DEFAULT_ERROR_MESSAGE}</p>
            <p className="mt-1">Vérifiez votre connexion puis réessayez.</p>
            <Button className="mt-4" variant="secondary" onClick={handleRetry}>
              Réessayer
            </Button>
          </div>
        )}

        {isRedirecting && (
          <div className="rounded-2xl border border-slate-200 bg-white/80 p-6 text-center shadow-sm dark:border-slate-700/70 dark:bg-slate-900/60">
            <LoadingIndicator label="Redirection en cours…" />
            <p className="mt-4 text-sm text-slate-600 dark:text-slate-300">
              Merci de patienter pendant la redirection vers l’accueil.
            </p>
          </div>
        )}

        {shouldShowForm && (
          <form className="space-y-6" onSubmit={(event) => event.preventDefault()}>
            <label
              className="block text-sm font-medium text-slate-700 dark:text-slate-200"
              htmlFor="shop-select"
              id={labelId}
            >
              Boutique
            </label>
            <div className="space-y-4">
              {useCardLayout ? (
                <>
                  <p id={cardsLabelId} className="sr-only">
                    Boutiques disponibles
                  </p>
                  <div aria-labelledby={cardsLabelId} className="space-y-3" role="radiogroup">
                    {shops.map((item, index) => {
                      const isSelected = item.id === selectedId
                      return (
                        <button
                          key={item.id}
                          ref={(element) => {
                            cardRefs.current[index] = element
                          }}
                          type="button"
                          role="radio"
                          aria-checked={isSelected}
                          onClick={() => handleCardSelect(item.id)}
                          className={clsx(
                            'w-full rounded-2xl border px-5 py-4 text-left text-base shadow-sm transition focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-300',
                            isSelected
                              ? 'border-brand-500 bg-brand-50 text-brand-800 dark:border-brand-400 dark:bg-brand-500/10 dark:text-brand-200'
                              : 'border-slate-300 bg-white text-slate-900 hover:border-brand-300 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-100'
                          )}
                        >
                          <span className="block text-lg font-medium">{item.name}</span>
                          <span className="mt-1 block text-sm text-slate-600 dark:text-slate-300">Appuyez pour choisir cette boutique.</span>
                        </button>
                      )
                    })}
                  </div>
                  <select
                    id="shop-select"
                    ref={selectRef}
                    className="sr-only"
                    value={selectedId}
                    onChange={onSelectChange}
                    aria-describedby={shops.length === 0 ? 'shop-help' : undefined}
                    aria-labelledby={labelId}
                  >
                    <option value="">Sélectionner une boutique</option>
                    {shops.map((item) => (
                      <option key={item.id} value={item.id}>
                        {item.name}
                      </option>
                    ))}
                  </select>
                </>
              ) : (
                <select
                  id="shop-select"
                  ref={selectRef}
                  className={clsx(
                    'w-full rounded-2xl border border-slate-300 bg-white px-4 py-4 text-base shadow-sm focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-200 dark:border-slate-700 dark:bg-slate-900',
                    selectedId
                      ? 'text-slate-900 dark:text-slate-100'
                      : 'text-slate-600 dark:text-slate-300'
                  )}
                  value={selectedId}
                  onChange={onSelectChange}
                  aria-describedby={shops.length === 0 ? 'shop-help' : undefined}
                  aria-labelledby={labelId}
                >
                  <option value="">Sélectionner une boutique</option>
                  {shops.map((item) => (
                    <option key={item.id} value={item.id}>
                      {item.name}
                    </option>
                  ))}
                </select>
              )}
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
