import {
  type ChangeEvent,
  useCallback,
  useEffect,
  useId,
  useMemo,
  useRef,
  useState,
} from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import clsx from 'clsx'

import { fetchShops } from '@/api/shops'
import { Page } from '@/app/components/Page'
import { LoadingIndicator } from '@/app/components/LoadingIndicator'
import { Button } from '@/app/components/ui/Button'
import type { Shop } from '@/types/shop'
import { useShop } from '@/state/ShopContext'
import { clearSelectedUserForShop } from '@/lib/selectedUserStorage'
import { useInventory } from '@/app/contexts/InventoryContext'

const DEFAULT_ERROR_MESSAGE = "Impossible de charger les boutiques."
const INVALID_GUID_ERROR_MESSAGE = 'Identifiant de boutique invalide. Vérifie le code et réessaie.'
const GUID_REGEX = /^[0-9a-f]{8}-([0-9a-f]{4}-){3}[0-9a-f]{12}$/i

const isValidGuid = (value: string) => GUID_REGEX.test(value)

type LoadingState = 'idle' | 'loading' | 'error'

type RedirectState = {
  redirectTo?: string
} | null

export const SelectShopPage = () => {
  const { shop, setShop } = useShop()
  const { reset } = useInventory()
  const navigate = useNavigate()
  const location = useLocation()
  const [shops, setShops] = useState<Shop[]>([])
  const [status, setStatus] = useState<LoadingState>('loading')
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [retryCount, setRetryCount] = useState<number>(0)
  const [selectedShopId, setSelectedShopId] = useState(() => shop?.id ?? '')
  const [selectionError, setSelectionError] = useState<string | null>(null)
  const [isRedirecting, setIsRedirecting] = useState(false)
  const labelId = useId()
  const cardsLabelId = useId()
  const selectRef = useRef<HTMLSelectElement | null>(null)
  const cardRefs = useRef<Array<HTMLButtonElement | null>>([])

  const redirectState = location.state as RedirectState
  const redirectTo = useMemo(() => {
    const target = redirectState?.redirectTo
    if (typeof target !== 'string') {
      return null
    }
    const normalized = target.trim()
    return normalized.length > 0 ? normalized : null
  }, [redirectState])

  useEffect(() => {
  const ac = new AbortController()
  let disposed = false

  const run = async () => {
    try {
      setStatus('loading')
      setErrorMessage(null)

      const list = await fetchShops(ac.signal)
      if (disposed) return

      setShops(list)
      setStatus('idle')
    } catch (e: unknown) {
      if (disposed) return

      if (
        (e instanceof DOMException && e.name === 'AbortError') ||
        (e instanceof Error && e.name === 'AbortError')
      ) {
        return
      }

      let msg = ''
      if (e instanceof Error) msg = e.message
      else if (typeof e === 'string') msg = e
      else if (typeof e === 'object' && e !== null && 'message' in e)
        msg = String((e as { message?: string }).message ?? '')

      if (msg === 'ABORTED' || msg.toLowerCase().includes('aborted')) return

      setErrorMessage(msg || 'Erreur de chargement')
      setStatus('error')
    }
  }

  run()
  return () => {
    disposed = true
    ac.abort('route-change')
  }
}, [retryCount]) 
  useEffect(() => {
    setSelectedShopId((current) => {
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

  useEffect(() => {
    if (!selectedShopId) {
      if (selectionError === INVALID_GUID_ERROR_MESSAGE) {
        setSelectionError(null)
      }
      return
    }

    if (!isValidGuid(selectedShopId)) {
      if (selectionError !== INVALID_GUID_ERROR_MESSAGE) {
        setSelectionError(INVALID_GUID_ERROR_MESSAGE)
      }
      return
    }

    if (selectionError === INVALID_GUID_ERROR_MESSAGE) {
      setSelectionError(null)
    }
  }, [selectedShopId, selectionError])

  useEffect(() => {
    if (status !== 'idle' || isRedirecting) {
      return
    }

    if (shops.length === 0) {
      selectRef.current?.focus()
      return
    }

    const useCardLayout = shops.length <= 5
    if (useCardLayout) {
      const targetIndex = selectedShopId ? shops.findIndex((item) => item.id === selectedShopId) : 0
      const fallbackIndex = targetIndex >= 0 ? targetIndex : 0
      const target = cardRefs.current[fallbackIndex]
      target?.focus()
      return
    }

    selectRef.current?.focus()
  }, [isRedirecting, selectedShopId, shops, status])

  const handleRetry = useCallback(() => {
    setRetryCount((count) => count + 1)
    setSelectionError(null)
  }, [])

  const continueWithShop = useCallback(
    (shopToActivate: Shop | null) => {
      if (isRedirecting) {
        return
      }

      if (!shopToActivate) {
        setSelectionError('Sélectionne une boutique pour continuer.')
        selectRef.current?.focus()
        return
      }

      if (!isValidGuid(shopToActivate.id)) {
        setSelectionError(INVALID_GUID_ERROR_MESSAGE)
        selectRef.current?.focus()
        return
      }

      if (!shop || shop.id !== shopToActivate.id) {
        reset()
        clearSelectedUserForShop(shopToActivate.id)
      }

      setSelectionError(null)
      setIsRedirecting(true)
      setShop(shopToActivate)

      const navigationOptions = redirectTo ? { state: { redirectTo } } : undefined
      navigate('/select-user', navigationOptions)
    },
    [isRedirecting, navigate, redirectTo, reset, shop, setShop],
  )

  const handleShopSelection = useCallback(
    (id: string) => {
      setSelectedShopId(id)
      const shopToActivate = shops.find((item) => item.id === id) ?? null
      continueWithShop(shopToActivate)
    },
    [continueWithShop, shops],
  )

  const handleSelectChange = useCallback(
    (event: ChangeEvent<HTMLSelectElement>) => {
      const id = event.target.value
      setSelectedShopId(id)

      if (!id) {
        setSelectionError(null)
        return
      }

      const shopToActivate = shops.find((item) => item.id === id) ?? null
      continueWithShop(shopToActivate)
    },
    [continueWithShop, shops],
  )

  const useCardLayout = !isRedirecting && status === 'idle' && shops.length > 0 && shops.length <= 5
  const isLoadingShops = status === 'loading'
  const shouldShowShopError = status === 'error' && !isRedirecting
  const shouldShowShopForm = status === 'idle' && !isRedirecting

  return (
    <Page className="px-4 py-6 sm:px-6">
      <main className="flex flex-1 flex-col gap-8">
        <div>
          <h1 className="text-2xl font-semibold text-slate-900 dark:text-slate-100">Choisir une boutique</h1>
          <p className="mt-1 text-sm text-slate-600 dark:text-slate-300">
            Sélectionne ta boutique pour continuer vers l’identification.
          </p>
        </div>

        {isLoadingShops && <LoadingIndicator label="Chargement des boutiques…" />}

        {shouldShowShopError && (
          <div
            className="rounded-2xl border border-red-200 bg-red-50 p-4 text-sm text-red-800 dark:border-red-900/60 dark:bg-red-950/40 dark:text-red-200"
            role="alert"
          >
            <p className="font-semibold">{errorMessage ?? DEFAULT_ERROR_MESSAGE}</p>
            <p className="mt-1">Vérifie ta connexion puis réessaie.</p>
            <Button className="mt-4" variant="secondary" onClick={handleRetry}>
              Réessayer
            </Button>
          </div>
        )}

        {isRedirecting && (
          <div className="rounded-2xl border border-slate-200 bg-white/80 p-6 text-center shadow-sm dark:border-slate-700/70 dark:bg-slate-900/60">
            <LoadingIndicator label="Redirection en cours…" />
            <p className="mt-4 text-sm text-slate-600 dark:text-slate-300">
              Merci de patienter pendant la redirection vers l’identification.
            </p>
          </div>
        )}

        {shouldShowShopForm && (
          <>
            <form className="space-y-4" onSubmit={(event) => event.preventDefault()}>
              <label className="sr-only" htmlFor="shop-select" id={labelId}>
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
                        const isSelected = item.id === selectedShopId
                        return (
                          <button
                            key={item.id}
                            ref={(element) => {
                              cardRefs.current[index] = element
                            }}
                            type="button"
                            role="radio"
                            aria-checked={isSelected}
                            onClick={() => handleShopSelection(item.id)}
                            className={clsx(
                              'w-full rounded-2xl border px-5 py-4 text-left text-base shadow-sm transition focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-300',
                              isSelected
                                ? 'border-brand-500 bg-brand-50 text-brand-800 dark:border-brand-400 dark:bg-brand-500/10 dark:text-brand-200'
                                : 'border-slate-300 bg-white text-slate-900 hover:border-brand-300 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-100',
                            )}
                          >
                            <span className="block text-lg font-medium">{item.name}</span>
                            <span className="mt-1 block text-sm text-slate-600 dark:text-slate-300">
                              Appuie pour choisir cette boutique et continuer.
                            </span>
                          </button>
                        )
                      })}
                    </div>
                    <select
                      id="shop-select"
                      ref={selectRef}
                      className="sr-only"
                      value={selectedShopId}
                      onChange={handleSelectChange}
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
                      selectedShopId ? 'text-slate-900 dark:text-slate-100' : 'text-slate-600 dark:text-slate-300',
                    )}
                    value={selectedShopId}
                    onChange={handleSelectChange}
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
                {selectionError && (
                  <p className="text-sm text-red-600 dark:text-red-400" role="alert">
                    {selectionError}
                  </p>
                )}
              </div>
            </form>

          </>
        )}
      </main>
    </Page>
  )
}

export default SelectShopPage
