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

type RedirectState = {
  redirectTo?: string
} | null

const ENTITY_BUTTON_BASE_CLASSES = clsx(
  'w-full rounded-lg border bg-white hover:bg-gray-50 text-gray-900 shadow-sm px-4 py-3 focus-visible:ring-2 focus-visible:ring-sky-500 ring-offset-2 focus:outline-none',
  'flex h-full flex-col items-start justify-between gap-3 text-left text-base transition dark:border-slate-700 dark:bg-slate-900 dark:text-slate-100 dark:hover:bg-slate-900/80 dark:focus-visible:ring-sky-400 dark:ring-offset-slate-900',
)

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
  const labelId = useId()
  const cardsLabelId = useId()
  const cardRefs = useRef<Array<HTMLButtonElement | null>>([])

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
  }, [retryCount])
  useEffect(() => {
    if (!shop) {
      startTransition(() => {
        setSelectedShopId('')
        setSelectedEntityId((current) => {
          if (!current) {
            return null
          }

          const entity = entityCards.find((card) => card.definition.id === current)
          if (!entity || entity.matches.length === 0) {
            return null
          }
          return current
        })
      })
      return
    }

    const entity = entityByShopId.get(shop.id) ?? null
    startTransition(() => {
      setSelectedShopId(shop.id)
      setSelectedEntityId(entity?.definition.id ?? null)
    })
  }, [entityByShopId, entityCards, shop])

  useEffect(() => {
    if (entityCards.length === 0) {
      startTransition(() => {
        setSelectedEntityId(null)
        setSelectedShopId('')
      })
      return
    }

    const currentSelection = selectedEntityId
      ? entityCards.find((card) => card.definition.id === selectedEntityId)
      : null

    if (currentSelection && currentSelection.matches.length === 0) {
      startTransition(() => {
        setSelectedEntityId(null)
        setSelectedShopId('')
      })
      return
    }

    if (selectedEntityId) {
      return
    }

    const fallback = entityCards.find((card) => card.matches.length > 0)
    if (!fallback) {
      return
    }

    startTransition(() => {
      setSelectedEntityId(fallback.definition.id)
      setSelectedShopId((current) => {
        if (current && fallback.matches.some((shopOption) => shopOption.id === current)) {
          return current
        }

        if (shop && fallback.matches.some((shopOption) => shopOption.id === shop.id)) {
          return shop.id
        }

        return ''
      })
    })
  }, [entityCards, selectedEntityId, shop])

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
      if (card.matches.length === 0) continue
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
      if (entity.matches.length === 0) {
        setSelectionError("Cette entité n’est pas encore disponible.")
        focusFirstAvailableCard()
        return
      }

      setSelectionError(null)
      startTransition(() => {
        setSelectedEntityId(entity.definition.id)
        setSelectedShopId((current) => {
          if (current && entity.matches.some((shopOption) => shopOption.id === current)) {
            return current
          }

          if (shop && entity.matches.some((shopOption) => shopOption.id === shop.id)) {
            return shop.id
          }

          return ''
        })
      })
    },
    [focusFirstAvailableCard, shop],
  )

  const isLoadingShops = status === 'loading'
  const shouldShowShopError = status === 'error' && !isRedirecting
  const shouldShowShopForm = status === 'idle' && !isRedirecting
  const selectedEntity = useMemo(
    () => entityCards.find((card) => card.definition.id === selectedEntityId) ?? null,
    [entityCards, selectedEntityId],
  )
  const allEntitiesUnavailable = entityCards.length > 0 && entityCards.every((card) => card.matches.length === 0)

  const handleShopSelection = useCallback(
    (shopToActivate: Shop) => {
      setSelectedShopId(shopToActivate.id)
      continueWithShop(shopToActivate)
    },
    [continueWithShop],
  )

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
            <form className="space-y-5" onSubmit={(event) => event.preventDefault()}>
              <fieldset className="space-y-4 border-0 p-0">
                <legend id={cardsLabelId} className="sr-only">
                  Boutiques disponibles
                </legend>
                <div
                  aria-labelledby={cardsLabelId}
                  className="space-y-3 sm:grid sm:grid-cols-2 sm:gap-3 sm:space-y-0"
                  role="radiogroup"
                >
                  {entityCards.map((card, index) => {
                    const isSelected = card.definition.id === selectedEntityId
                    const isDisabled = card.matches.length === 0

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
                        aria-label={
                          isDisabled
                            ? `${card.definition.label} indisponible pour le moment`
                            : card.definition.label
                        }
                        disabled={isDisabled}
                        onClick={() => handleEntitySelection(card)}
                        className={clsx(
                          ENTITY_BUTTON_BASE_CLASSES,
                          isSelected
                            ? 'border-sky-500 ring-1 ring-sky-200 dark:border-sky-500 dark:ring-sky-500/40'
                            : 'border-slate-200/80 dark:border-slate-700',
                          isDisabled &&
                            'cursor-not-allowed opacity-60 hover:bg-white focus-visible:ring-0 focus-visible:ring-offset-0 dark:hover:bg-slate-900',
                        )}
                      >
                        <span className="text-lg font-semibold">
                          {card.definition.label}
                        </span>
                        <span className="text-sm text-slate-600 dark:text-slate-300">
                          {card.definition.description}
                        </span>
                        <span
                          className={clsx(
                            'inline-flex items-center rounded-full px-3 py-1 text-xs font-medium uppercase tracking-wide transition-colors',
                            isDisabled
                              ? 'bg-gray-100 text-gray-500 dark:bg-slate-800 dark:text-slate-400'
                              : isSelected
                              ? 'bg-sky-100 text-sky-700 dark:bg-sky-500/20 dark:text-sky-100'
                              : 'bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-300',
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
              {selectedEntity && (
                <section
                  aria-labelledby={`${labelId}-shops`}
                  className="space-y-3 rounded-2xl border border-slate-200/80 bg-white/70 p-4 shadow-sm backdrop-blur-sm dark:border-slate-700/60 dark:bg-slate-900/40"
                >
                  <div className="space-y-1">
                    <h2 id={`${labelId}-shops`} className="text-lg font-semibold text-slate-900 dark:text-slate-100">
                      {selectedEntity.definition.label}
                    </h2>
                    <p className="text-sm text-slate-600 dark:text-slate-300">
                      Choisis la boutique pour continuer vers l’identification.
                    </p>
                  </div>
                  {selectedEntity.matches.length > 0 ? (
                    <ul className="grid gap-2">
                      {selectedEntity.matches.map((shopOption) => {
                        const isActive = shopOption.id === selectedShopId
                        return (
                          <li key={shopOption.id}>
                            <button
                              type="button"
                              onClick={() => handleShopSelection(shopOption)}
                              className={clsx(
                                'flex w-full items-center justify-between rounded-xl border px-4 py-3 text-left text-sm font-medium transition focus:outline-none focus-visible:ring-2 focus-visible:ring-offset-2',
                                isActive
                                  ? 'border-brand-500 bg-brand-500/10 text-brand-900 shadow-sm focus-visible:ring-brand-200 focus-visible:ring-offset-white dark:border-brand-400 dark:bg-brand-400/20 dark:text-brand-50 dark:focus-visible:ring-brand-300 dark:focus-visible:ring-offset-slate-900'
                                  : 'border-slate-200 bg-white/70 text-slate-900 hover:border-brand-300 hover:bg-brand-50/30 focus-visible:ring-brand-200 focus-visible:ring-offset-white dark:border-slate-700 dark:bg-slate-900/60 dark:text-slate-100 dark:hover:border-brand-400 dark:hover:bg-brand-400/10 dark:focus-visible:ring-brand-300 dark:focus-visible:ring-offset-slate-900',
                              )}
                            >
                              <span className="flex-1 truncate">{shopOption.name}</span>
                              {isActive && (
                                <span className="ml-3 inline-flex items-center rounded-full bg-brand-500 px-3 py-1 text-xs font-semibold uppercase tracking-wide text-white shadow-sm dark:bg-brand-400 dark:text-slate-900">
                                  Sélectionné
                                </span>
                              )}
                            </button>
                          </li>
                        )
                      })}
                    </ul>
                  ) : (
                    <p className="text-sm text-slate-500 dark:text-slate-300">
                      Aucune boutique n’est disponible pour le moment.
                    </p>
                  )}
                </section>
              )}
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
