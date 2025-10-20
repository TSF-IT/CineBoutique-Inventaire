import { startTransition, useCallback, useEffect, useId, useMemo, useRef, useState } from 'react'
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
import { buildEntityCards, type EntityCardModel, type EntityId } from './entities'

const DEFAULT_ERROR_MESSAGE = "Impossible de charger les boutiques."
const INVALID_GUID_ERROR_MESSAGE = 'Identifiant de boutique invalide. Vérifie le code et réessaie.'
const GUID_REGEX = /^[0-9a-f]{8}-([0-9a-f]{4}-){3}[0-9a-f]{12}$/i

const isValidGuid = (value: string) => GUID_REGEX.test(value)

type LoadingState = 'idle' | 'loading' | 'error'

type ShopFilter = 'all' | 'boutique' | 'lumiere'

type RedirectState = {
  redirectTo?: string
} | null

type EntityTheme = {
  selected: string
  idle: string
  focusRing: string
  overlaySelected: string
  overlayIdle: string
  badge: string
}

const ENTITY_THEMES: Record<EntityId, EntityTheme> = {
  cineboutique: {
    selected:
      'border-transparent bg-gradient-to-br from-brand-500 to-brand-600 text-white shadow-md dark:from-brand-400 dark:to-brand-500',
    idle:
      'border-slate-200/70 bg-slate-900/5 text-slate-900 hover:border-brand-300 hover:bg-slate-900/10 dark:border-slate-700 dark:bg-white/5 dark:text-slate-100',
    focusRing: 'focus-visible:ring-brand-200 dark:focus-visible:ring-brand-300',
    overlaySelected: 'bg-white/10',
    overlayIdle: 'bg-brand-500/10 dark:bg-brand-400/10',
    badge: 'bg-white/20 text-white dark:bg-slate-900/40 dark:text-slate-100',
  },
  lumiere: {
    selected:
      'border-transparent bg-gradient-to-br from-indigo-500 via-purple-500 to-fuchsia-500 text-white shadow-md dark:from-indigo-400 dark:via-purple-400 dark:to-fuchsia-400',
    idle:
      'border-slate-200/70 bg-slate-900/5 text-slate-900 hover:border-indigo-300 hover:bg-slate-900/10 dark:border-slate-700 dark:bg-white/5 dark:text-slate-100',
    focusRing: 'focus-visible:ring-indigo-200 dark:focus-visible:ring-indigo-300',
    overlaySelected: 'bg-white/10',
    overlayIdle: 'bg-indigo-500/10 dark:bg-indigo-400/20',
    badge: 'bg-white/20 text-white dark:bg-slate-900/40 dark:text-slate-100',
  },
}

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
  const [selectedEntityId, setSelectedEntityId] = useState<EntityId | null>(null)
  const [selectionError, setSelectionError] = useState<string | null>(null)
  const [isRedirecting, setIsRedirecting] = useState(false)
  const [filter, setFilter] = useState<ShopFilter>('all')
  const labelId = useId()
  const cardsLabelId = useId()
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

        const list = await fetchShops({
          signal: ac.signal,
          kind: filter === 'all' ? undefined : filter,
        })
        if (disposed) {
          return
        }

        setShops(list)
        setStatus('idle')
      } catch (e: unknown) {
        if (disposed) {
          return
        }

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

        if (msg === 'ABORTED' || msg.toLowerCase().includes('aborted')) {
          return
        }

        setErrorMessage(msg || 'Erreur de chargement')
        setStatus('error')
      }
    }

    run()
    return () => {
      disposed = true
      ac.abort('route-change')
    }
  }, [filter, retryCount])
  useEffect(() => {
    startTransition(() => {
      setSelectedShopId((current) => {
        if (shops.length === 0) {
          return shop?.id ?? ''
        }

        if (current && shops.some((item) => item.id === current)) {
          return current
        }

        const preferredShopId = shop?.id
        if (preferredShopId && shops.some((item) => item.id === preferredShopId)) {
          return preferredShopId
        }

        return shops[0]?.id ?? ''
      })
    })
  }, [shop?.id, shops])

  const entityCards = useMemo(() => buildEntityCards(shops), [shops])

  const entityByShopId = useMemo(() => {
    const map = new Map<string, EntityCardModel>()
    for (const card of entityCards) {
      for (const candidate of card.matches) {
        map.set(candidate.id, card)
      }
    }
    return map
  }, [entityCards])

  useEffect(() => {
    startTransition(() => {
      if (!shop) {
        setSelectedShopId('')
        setSelectedEntityId(null)
        return
      }

      const entity = entityByShopId.get(shop.id) ?? null
      setSelectedShopId(shop.id)
      setSelectedEntityId(entity?.definition.id ?? null)
    })
  }, [entityByShopId, shop])

  useEffect(() => {
    if (!selectedShopId) {
      if (selectionError === INVALID_GUID_ERROR_MESSAGE) {
        startTransition(() => {
          setSelectionError(null)
        })
      }
      return
    }

    if (!isValidGuid(selectedShopId)) {
      if (selectionError !== INVALID_GUID_ERROR_MESSAGE) {
        startTransition(() => {
          setSelectionError(INVALID_GUID_ERROR_MESSAGE)
        })
      }
      return
    }

    if (selectionError === INVALID_GUID_ERROR_MESSAGE) {
      startTransition(() => {
        setSelectionError(null)
      })
    }
  }, [selectedShopId, selectionError])

  const focusFirstAvailableCard = useCallback(() => {
    for (let index = 0; index < entityCards.length; index += 1) {
      const card = entityCards[index]
      if (!card.primaryShop) continue
      const element = cardRefs.current[index]
      if (element) {
        element.focus()
        return
      }
    }
  }, [entityCards])

  useEffect(() => {
    if (status !== 'idle' || isRedirecting || entityCards.length === 0) {
      return
    }

    const targetIndex = selectedEntityId
      ? entityCards.findIndex((item) => item.definition.id === selectedEntityId)
      : 0
    const fallbackIndex = targetIndex >= 0 ? targetIndex : 0
    const target = cardRefs.current[fallbackIndex]
    if (target) {
      target.focus()
      return
    }

    focusFirstAvailableCard()
  }, [entityCards, focusFirstAvailableCard, isRedirecting, selectedEntityId, status])

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
        focusFirstAvailableCard()
        return
      }

      if (!isValidGuid(shopToActivate.id)) {
        setSelectionError(INVALID_GUID_ERROR_MESSAGE)
        focusFirstAvailableCard()
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
    [focusFirstAvailableCard, isRedirecting, navigate, redirectTo, reset, shop, setShop],
  )

  const handleEntitySelection = useCallback(
    (entity: EntityCardModel) => {
      if (!entity.primaryShop) {
        setSelectionError("Cette entité n’est pas encore disponible.")
        focusFirstAvailableCard()
        return
      }

      setSelectedEntityId(entity.definition.id)
      setSelectedShopId(entity.primaryShop.id)
      continueWithShop(entity.primaryShop)
    },
    [continueWithShop, focusFirstAvailableCard],
  )

  const isLoadingShops = status === 'loading'
  const shouldShowShopError = status === 'error' && !isRedirecting
  const shouldShowShopForm = status === 'idle' && !isRedirecting
  const filterOptions: readonly ShopFilter[] = ['all', 'boutique', 'lumiere'] as const
  const allEntitiesUnavailable = entityCards.length > 0 && entityCards.every((card) => !card.primaryShop)

  return (
    <Page className="px-4 py-6 sm:px-6">
      <main className="flex flex-1 flex-col gap-8">
        <div>
          <h1 className="text-2xl font-semibold text-slate-900 dark:text-slate-100">Choisir une entité</h1>
          <p className="mt-1 text-sm text-slate-600 dark:text-slate-300">
            Sélectionne ton entité pour continuer vers l’identification.
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
            <div
              style={{
                position: 'sticky',
                top: 0,
                padding: '8px 0',
                background: '#fff',
                zIndex: 1,
              }}
            >
              <div
                style={{
                  display: 'inline-flex',
                  border: '1px solid #ccc',
                  borderRadius: 999,
                  overflow: 'hidden',
                }}
              >
                {filterOptions.map((key) => (
                  <button
                    key={key}
                    type="button"
                    onClick={() => setFilter(key)}
                    style={{
                      padding: '6px 12px',
                      border: 'none',
                      background: filter === key ? '#111' : '#fff',
                      color: filter === key ? '#fff' : '#111',
                      cursor: 'pointer',
                    }}
                  >
                    {key === 'all' ? 'Toutes' : key === 'boutique' ? 'CinéBoutique' : 'Lumière'}
                  </button>
                ))}
              </div>
            </div>
            <form className="space-y-4" onSubmit={(event) => event.preventDefault()}>
              <fieldset className="space-y-4 border-0 p-0">
                <legend id={cardsLabelId} className="sr-only">
                  Boutiques disponibles
                </legend>
                <div aria-labelledby={cardsLabelId} className="grid gap-3 sm:grid-cols-2" role="radiogroup">
                  {entityCards.map((card, index) => {
                    const theme = ENTITY_THEMES[card.definition.id]
                    const isSelected = card.primaryShop?.id === selectedShopId
                    const isDisabled = !card.primaryShop

                    return (
                      <button
                        key={card.definition.id}
                        ref={(element) => {
                          cardRefs.current[index] = element
                        }}
                        type="button"
                        role="radio"
                        aria-checked={isSelected}
                        aria-disabled={isDisabled}
                        disabled={isDisabled}
                        onClick={() => handleEntitySelection(card)}
                        className={clsx(
                          'group relative flex h-full w-full flex-col justify-between overflow-hidden rounded-2xl border px-5 py-4 text-left text-base shadow-sm transition duration-200 focus:outline-none focus-visible:ring-2',
                          isSelected
                            ? theme.selected
                            : clsx(theme.idle, isDisabled && 'cursor-not-allowed opacity-70'),
                          isDisabled ? 'focus-visible:ring-transparent' : theme.focusRing,
                        )}
                      >
                        <span
                          aria-hidden="true"
                          className={clsx(
                            'pointer-events-none absolute inset-0 translate-y-2 opacity-0 transition duration-200 group-hover:translate-y-0 group-hover:opacity-100',
                            isSelected ? theme.overlaySelected : theme.overlayIdle,
                          )}
                        />
                        <span className="relative block text-lg font-semibold">{card.definition.label}</span>
                        <span className="relative mt-2 block text-sm text-slate-600 dark:text-slate-300">
                          {card.definition.description}
                        </span>
                        <span
                          className={clsx(
                            'relative mt-3 inline-flex w-fit items-center rounded-full px-3 py-1 text-xs font-semibold uppercase tracking-wide',
                            isDisabled
                              ? 'bg-slate-200 text-slate-600 dark:bg-slate-800 dark:text-slate-300'
                              : theme.badge,
                          )}
                        >
                          {isDisabled
                            ? 'Bientôt disponible'
                            : card.matches.length === 1
                            ? '1 boutique disponible'
                            : `${card.matches.length} boutiques disponibles`}
                        </span>
                      </button>
                    )
                  })}
                </div>
              </fieldset>
              {allEntitiesUnavailable && (
                <p id="shop-help" className="text-sm text-slate-500 dark:text-slate-300">
                  Aucune entité n’est disponible pour le moment.
                </p>
              )}
              {selectionError && (
                <p className="text-sm text-red-600 dark:text-red-400" role="alert">
                  {selectionError}
                </p>
              )}
            </form>

          </>
        )}
      </main>
    </Page>
  )
}

export default SelectShopPage
