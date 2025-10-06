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
import { fetchShopUsers } from '@/app/api/shopUsers'
import { Page } from '@/app/components/Page'
import { LoadingIndicator } from '@/app/components/LoadingIndicator'
import { Button } from '@/app/components/ui/Button'
import { Input } from '@/app/components/ui/Input'
import type { Shop } from '@/types/shop'
import type { ShopUser } from '@/types/user'
import { useShop } from '@/state/ShopContext'
import { clearShop, loadShop } from '@/lib/shopStorage'
import {
  clearSelectedUserForShop,
  loadSelectedUserForShop,
  saveSelectedUserForShop,
} from '@/lib/selectedUserStorage'
import { useInventory } from '@/app/contexts/InventoryContext'

const DEFAULT_ERROR_MESSAGE = "Impossible de charger les boutiques."
const DEFAULT_USER_ERROR_MESSAGE = "Impossible de charger les utilisateurs de la boutique sélectionnée."
const GUID_REGEX = /^[0-9a-f]{8}-([0-9a-f]{4}-){3}[0-9a-f]{12}$/i

const isValidGuid = (value: string) => GUID_REGEX.test(value)

const extractStoredUserId = (stored: ReturnType<typeof loadSelectedUserForShop>): string | null => {
  if (!stored) {
    return null
  }

  if ('userId' in stored && typeof stored.userId === 'string') {
    const candidate = stored.userId.trim()
    return candidate.length > 0 ? candidate : null
  }

  if ('id' in stored) {
    const candidate = typeof stored.id === 'string' ? stored.id.trim() : ''
    return candidate.length > 0 ? candidate : null
  }

  return null
}

type LoadingState = 'idle' | 'loading' | 'error'

type RedirectState = {
  redirectTo?: string
} | null

export const SelectShopPage = () => {
  const { shop, setShop } = useShop()
  const { selectedUser, setSelectedUser, reset } = useInventory()
  const navigate = useNavigate()
  const location = useLocation()
  const [shops, setShops] = useState<Shop[]>([])
  const [status, setStatus] = useState<LoadingState>('loading')
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [retryCount, setRetryCount] = useState(0)
  const [selectedShopId, setSelectedShopId] = useState(() => shop?.id ?? '')
  const [selectionError, setSelectionError] = useState<string | null>(null)
  const [isRedirecting, setIsRedirecting] = useState(false)
  const [users, setUsers] = useState<ShopUser[]>([])
  const [usersStatus, setUsersStatus] = useState<LoadingState>('idle')
  const [usersErrorMessage, setUsersErrorMessage] = useState<string | null>(null)
  const [selectedUserId, setSelectedUserId] = useState<string>('')
  const [search, setSearch] = useState('')
  const labelId = useId()
  const cardsLabelId = useId()
  const userCardsLabelId = useId()
  const selectRef = useRef<HTMLSelectElement | null>(null)
  const cardRefs = useRef<Array<HTMLButtonElement | null>>([])
  const userCardRefs = useRef<Array<HTMLButtonElement | null>>([])
  const previousShopIdRef = useRef<string | null>(shop?.id ?? null)

  const redirectState = location.state as RedirectState
  const redirectTo = useMemo(() => {
    const target = redirectState?.redirectTo
    if (typeof target !== 'string') {
      return null
    }
    const normalized = target.trim()
    return normalized.length > 0 ? normalized : null
  }, [redirectState])

  const activeShop = useMemo(
    () => shops.find((item) => item.id === selectedShopId) ?? null,
    [selectedShopId, shops],
  )

  const filteredUsers = useMemo(() => {
    if (!search.trim()) {
      return users
    }
    const normalizedQuery = search.trim().toLowerCase()
    return users.filter((user) => user.displayName.toLowerCase().includes(normalizedQuery))
  }, [search, users])

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
        const savedShop = loadShop()
        if (savedShop && !data.some((item) => item.id === savedShop.id)) {
          clearShop()
        }
        setStatus('idle')
      })
      .catch((error) => {
        if (!isMounted) {
          return
        }
        if (error instanceof DOMException && error.name === 'AbortError') {
          return
        }
        if (import.meta.env.DEV) {
          console.warn('[select-shop] échec du chargement des boutiques', error)
        }
        setShops([])
        setStatus('error')
        if (error instanceof TypeError) {
          setErrorMessage(
            'Connexion impossible. Vérifie ta connexion réseau puis réessaie. Vérifie également que l’API est lancée et que le proxy Vite pointe la bonne origine via DEV_BACKEND_ORIGIN.'
          )
          return
        }
        setErrorMessage(
          'Impossible de charger la liste des boutiques. Vérifie que l’API est lancée et que le proxy Vite pointe la bonne origine via DEV_BACKEND_ORIGIN.'
        )
      })

    return () => {
      isMounted = false
      controller.abort()
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
    if (status !== 'idle' || isRedirecting) {
      return
    }

    if (shops.length === 0) {
      selectRef.current?.focus()
      return
    }

    const useCardLayout = shops.length <= 5
    if (useCardLayout) {
      const targetIndex = selectedShopId
        ? shops.findIndex((item) => item.id === selectedShopId)
        : 0
      const fallbackIndex = targetIndex >= 0 ? targetIndex : 0
      const target = cardRefs.current[fallbackIndex]
      target?.focus()
      return
    }

    selectRef.current?.focus()
  }, [isRedirecting, selectedShopId, shops, status])

  useEffect(() => {
    if (!activeShop) {
      setUsers([])
      setUsersStatus('idle')
      setUsersErrorMessage(null)
      setSelectedUserId('')
      return
    }

    if (!isValidGuid(activeShop.id)) {
      setSelectionError('Identifiant de boutique invalide. Vérifiez le code et réessayez.')
      setUsers([])
      setUsersStatus('idle')
      setUsersErrorMessage(null)
      setSelectedUserId('')
      return
    }

    setSelectionError(null)

    if (!shop || shop.id !== activeShop.id) {
      setShop(activeShop)
    }

    if (previousShopIdRef.current !== activeShop.id) {
      previousShopIdRef.current = activeShop.id
      setSearch('')
      setSelectedUserId('')
      clearSelectedUserForShop(activeShop.id)
      reset()
    }

    let isCancelled = false
    setUsersStatus('loading')
    setUsersErrorMessage(null)
    setUsers([])

    Promise.resolve(fetchShopUsers(activeShop.id))
      .then((data) => {
        if (isCancelled) {
          return
        }
        const source = Array.isArray(data) ? data : []
        const availableUsers = source.filter((user) => !user.disabled)
        setUsers(availableUsers)
        setUsersStatus('idle')

        const storedUserId = extractStoredUserId(loadSelectedUserForShop(activeShop.id))
        if (!storedUserId) {
          clearSelectedUserForShop(activeShop.id)
          setSelectedUserId('')
          return
        }

        const storedUser = availableUsers.find((user) => user.id === storedUserId)
        if (!storedUser) {
          clearSelectedUserForShop(activeShop.id)
          setSelectedUserId('')
          return
        }

        setSelectedUserId(storedUser.id)
        if (!selectedUser || selectedUser.id !== storedUser.id) {
          setSelectedUser(storedUser)
        }
      })
      .catch((error) => {
        if (isCancelled) {
          return
        }

        if (import.meta.env.DEV) {
          console.error('[select-shop] Impossible de charger les utilisateurs de la boutique.', error)
        }

        setUsers([])
        setUsersStatus('error')

        if (error && typeof error === 'object' && '__shopNotFound' in error) {
          setUsersErrorMessage("La boutique sélectionnée n'existe plus. Merci de la re-sélectionner.")
          clearShop()
          setShop(null)
          setSelectedShopId('')
          return
        }

        setUsersErrorMessage(DEFAULT_USER_ERROR_MESSAGE)
      })

    return () => {
      isCancelled = true
    }
  }, [activeShop, reset, selectedUser, setSelectedUser, setShop, shop])

  const handleRetry = useCallback(() => {
    setRetryCount((count) => count + 1)
    setSelectionError(null)
  }, [])

  const handleShopSelection = useCallback(
    (id: string) => {
      setSelectedShopId(id)
      setSelectionError(null)
    },
    [],
  )

  const handleSelectChange = useCallback(
    (event: ChangeEvent<HTMLSelectElement>) => {
      const id = event.target.value
      setSelectedShopId(id)
      setSelectionError(null)
    },
    [],
  )

  useEffect(() => {
    if (usersStatus !== 'idle' || isRedirecting) {
      return
    }

    if (filteredUsers.length === 0) {
      return
    }

    const targetIndex = selectedUserId
      ? filteredUsers.findIndex((item) => item.id === selectedUserId)
      : 0
    const fallbackIndex = targetIndex >= 0 ? targetIndex : 0
    const target = userCardRefs.current[fallbackIndex]
    target?.focus()
  }, [filteredUsers, isRedirecting, selectedUserId, usersStatus])

  const completeSelection = useCallback(
    (user: ShopUser) => {
      if (!activeShop || isRedirecting) {
        return
      }

      if (!selectedUser || selectedUser.id !== user.id) {
        setSelectedUser(user)
      }

      saveSelectedUserForShop(activeShop.id, user)
      setSelectedUserId(user.id)
      setIsRedirecting(true)
      navigate(redirectTo ?? '/', { replace: true })
    },
    [activeShop, isRedirecting, navigate, redirectTo, selectedUser, setSelectedUser],
  )

  const handleUserSelect = useCallback(
    (user: ShopUser) => {
      completeSelection(user)
    },
    [completeSelection],
  )

  const handleContinue = useCallback(() => {
    if (!activeShop || !selectedUserId) {
      return
    }

    const user = users.find((item) => item.id === selectedUserId)
    if (!user) {
      return
    }

    completeSelection(user)
  }, [activeShop, completeSelection, selectedUserId, users])

  const useCardLayout = !isRedirecting && status === 'idle' && shops.length > 0 && shops.length <= 5
  const isLoadingShops = status === 'loading'
  const shouldShowShopError = status === 'error' && !isRedirecting
  const shouldShowShopForm = status === 'idle' && !isRedirecting
  const isLoadingUsers = usersStatus === 'loading'
  const shouldShowUsersError = usersStatus === 'error'
  const shouldShowUserSection = shouldShowShopForm && Boolean(activeShop)

  return (
    <Page className="px-4 py-6 sm:px-6">
      <main className="flex flex-1 flex-col gap-8">
        <div>
          <h1 className="text-2xl font-semibold text-slate-900 dark:text-slate-100">Choisir une boutique</h1>
          <p className="mt-1 text-sm text-slate-600 dark:text-slate-300">
            Sélectionne ta boutique puis ton profil pour accéder à l’inventaire.
          </p>
        </div>

        {isLoadingShops && (
          <LoadingIndicator label="Chargement des boutiques…" />
        )}

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
              Merci de patienter pendant la redirection vers l’accueil.
            </p>
          </div>
        )}

        {shouldShowShopForm && (
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
                            Appuie pour choisir cette boutique.
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
                    selectedShopId
                      ? 'text-slate-900 dark:text-slate-100'
                      : 'text-slate-600 dark:text-slate-300',
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
        )}

        {shouldShowUserSection && (
          <section className="space-y-4">
            <div>
              <h2 className="text-xl font-semibold text-slate-900 dark:text-slate-100">
                Qui réalise le comptage ?
              </h2>
              <p className="mt-1 text-sm text-slate-600 dark:text-slate-300">
                Sélectionne ton profil pour assurer la traçabilité des comptages.
              </p>
            </div>

            <Input
              label="Rechercher"
              name="user-search"
              placeholder="Rechercher un utilisateur"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              disabled={isLoadingUsers || isRedirecting}
            />

            {isLoadingUsers && (
              <div className="rounded-2xl border border-slate-200 bg-white/60 p-6 text-center dark:border-slate-700/60 dark:bg-slate-900/40">
                <LoadingIndicator label="Chargement des utilisateurs…" />
              </div>
            )}

            {shouldShowUsersError && usersErrorMessage && (
              <div
                className="rounded-2xl border border-red-200 bg-red-50 p-4 text-sm text-red-800 dark:border-red-900/60 dark:bg-red-950/40 dark:text-red-200"
                role="alert"
              >
                {usersErrorMessage}
              </div>
            )}

            {!isLoadingUsers && !shouldShowUsersError && filteredUsers.length === 0 && (
              <p className="text-sm text-slate-500 dark:text-slate-300">
                Aucun utilisateur ne correspond à ta recherche.
              </p>
            )}

            {!isLoadingUsers && filteredUsers.length > 0 && (
              <div aria-labelledby={userCardsLabelId} className="grid grid-cols-1 gap-3 sm:grid-cols-2" role="radiogroup">
                <p id={userCardsLabelId} className="sr-only">
                  Utilisateurs disponibles
                </p>
                {filteredUsers.map((user, index) => {
                  const isSelected = user.id === selectedUserId
                  return (
                    <button
                      key={user.id}
                      ref={(element) => {
                        userCardRefs.current[index] = element
                      }}
                      type="button"
                      role="radio"
                      aria-checked={isSelected}
                      onClick={() => handleUserSelect(user)}
                      disabled={isRedirecting}
                      className={clsx(
                        'rounded-2xl border px-5 py-4 text-left text-base shadow-sm transition focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-300',
                        isSelected
                          ? 'border-brand-500 bg-brand-50 text-brand-800 dark:border-brand-400 dark:bg-brand-500/10 dark:text-brand-200'
                          : 'border-slate-300 bg-white text-slate-900 hover:border-brand-300 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-100',
                      )}
                    >
                      <span className="block text-lg font-medium">{user.displayName}</span>
                      <span className="mt-1 block text-sm text-slate-600 dark:text-slate-300">
                        Appuie pour te connecter.
                      </span>
                    </button>
                  )
                })}
              </div>
            )}

            {selectedUserId && filteredUsers.some((user) => user.id === selectedUserId) && (
              <Button fullWidth className="py-4" disabled={isRedirecting} onClick={handleContinue}>
                Continuer
              </Button>
            )}
          </section>
        )}
      </main>
    </Page>
  )
}

export default SelectShopPage
