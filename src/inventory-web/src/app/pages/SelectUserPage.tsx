import { useEffect, useMemo, useRef, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import http from '@/lib/api/http'
import { API_BASE } from '@/lib/api/config'
import { useShop } from '@/state/ShopContext'
import { saveSelectedUserForShop } from '@/lib/selectedUserStorage'
import { z } from 'zod'
import { Page } from '@/app/components/Page'
import { LoadingIndicator } from '@/app/components/LoadingIndicator'
import { useInventory } from '@/app/contexts/InventoryContext'
import { clearShop } from '@/lib/shopStorage'
import { BackToShopSelectionLink } from '@/app/components/BackToShopSelectionLink'

type ShopUser = {
  id: string
  displayName: string
  email?: string | null
}

const ShopUserApiSchema = z.object({
  id: z.string().trim().min(1, 'Identifiant utilisateur manquant'),
  displayName: z.string().optional().default('Utilisateur'),
  email: z.string().email().nullable().optional()
}).passthrough()

const UsersApiSchema = z.array(ShopUserApiSchema)

const DEFAULT_ERROR_MESSAGE = 'Impossible de charger les utilisateurs.'

type RedirectState = { redirectTo?: string } | null

// Pour typer l'erreur sans any
type HttpLikeError = {
  name?: string
  message?: string
  status?: number
}

export default function SelectUserPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const { shop, isLoaded } = useShop()
  const { setSelectedUser } = useInventory()

  const [users, setUsers] = useState<ShopUser[]>([])
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)

  const redirectState = location.state as RedirectState
  const redirectTo = useMemo(() => {
    const target = redirectState?.redirectTo
    if (typeof target !== 'string') return null
    const normalized = target.trim()
    return normalized.length > 0 ? normalized : null
  }, [redirectState])

  // Empêche de traiter la réponse d’un fetch annulé/obsolète (StrictMode double-run)
  const requestIdRef = useRef(0)

  useEffect(() => {
    if (!isLoaded) return
    if (!shop) {
      const options = redirectTo ? { replace: true, state: { redirectTo } } : { replace: true }
      navigate('/select-shop', options)
      return
    }

    const ac = new AbortController()
    const myReqId = ++requestIdRef.current

    setLoading(true)
    setErr(null)

    const url = `${API_BASE}/shops/${encodeURIComponent(shop.id)}/users`

    ;(async () => {
      try {
        const res = await http(url, { signal: ac.signal })
        // Tolérance: si pas tableau, on force tableau vide au lieu d’échouer
        const arrUnknown: unknown[] = Array.isArray(res) ? res : []

        const parsed = UsersApiSchema.safeParse(arrUnknown)
        const normalized: ShopUser[] = parsed.success
          ? parsed.data.map(u => ({
              id: u.id,
              displayName: u.displayName ?? 'Utilisateur',
              email: u.email ?? null
            }))
          : []

        // Si la requête a été supplantée/annulée, on ignore
        if (requestIdRef.current !== myReqId || ac.signal.aborted) return

        setUsers(normalized)
      } catch (rawError: unknown) {
        const e = rawError as HttpLikeError

        // Ignore les annulations (AbortController ou NS_BINDING_ABORTED remonté par un wrapper)
        const msg = (e?.message ?? '').toString()
        const isAbort =
          e?.name === 'AbortError' ||
          msg === 'ABORTED' ||
          msg.toLowerCase().includes('aborted') ||
          msg.toUpperCase().includes('NS_BINDING_ABORTED')

        if (isAbort) return

        // 404 boutique changée/supprimée: purge puis retour au choix de boutique
        if (e?.status === 404) {
          clearShop()
          navigate('/select-shop', redirectTo ? { replace: true, state: { redirectTo } } : { replace: true })
          return
        }

        // 5xx: redirection douce vers le choix de boutique (évite un état cassé)
        if (typeof e?.status === 'number' && e.status >= 500) {
          clearShop()
          navigate('/select-shop', redirectTo ? { replace: true, state: { redirectTo } } : { replace: true })
          return
        }

        // Autres erreurs: message utilisateur
        setErr(msg || DEFAULT_ERROR_MESSAGE)
      } finally {
        if (requestIdRef.current === myReqId && !ac.signal.aborted) {
          setLoading(false)
        }
      }
    })()

    return () => {
      ac.abort()
    }
  }, [shop, isLoaded, navigate, redirectTo])

  const onPick = (u: ShopUser) => {
    if (!shop) return

    type Snapshot = Parameters<typeof saveSelectedUserForShop>[1]

    const loginCandidate =
      u.email && u.email.trim().length > 0 ? u.email.trim() : u.displayName

    const snapshot: Snapshot = {
      id: u.id,
      displayName: u.displayName,
      shopId: shop.id,
      login: loginCandidate,
      isAdmin: false,
      disabled: false
    }

    setSelectedUser(snapshot)
    saveSelectedUserForShop(shop.id, snapshot)
    navigate(redirectTo ?? '/', { replace: true })
  }

  if (!isLoaded || !shop) return null

  const shouldShowList = !loading && !err
  const hasUsers = users.length > 0
  const shopSelectionState = redirectTo ? { redirectTo } : undefined

  return (
    <Page
      className="gap-8"
      headerAction={
        <BackToShopSelectionLink
          state={shopSelectionState}
        />
      }
    >
      <main className="flex flex-1 flex-col justify-center gap-8">
        <div>
          <h1 className="cb-section-title text-3xl">
            Merci de vous identifier
          </h1>
        </div>

        {loading && <LoadingIndicator label="Chargement des utilisateurs…" />}

        {!loading && err && (
          <div
            className="rounded-2xl border border-red-200 bg-red-50 p-4 text-sm text-red-800 dark:border-red-900/60 dark:bg-red-950/40 dark:text-red-200"
            role="alert"
          >
            <p className="font-semibold">{err || DEFAULT_ERROR_MESSAGE}</p>
            <p className="mt-1">Vérifiez votre connexion puis réessayez.</p>
          </div>
        )}

        {shouldShowList && (
          <div className="space-y-4">
            <ul className="cards">
              {users.map(u => (
                <li key={u.id}>
                  <button
                    type="button"
                    onClick={() => onPick(u)}
                    className="block w-full rounded-3xl border border-(--cb-border-soft) bg-(--cb-surface-soft) px-5 py-4 text-left text-base text-(--cb-text) shadow-panel-soft transition hover:-translate-y-0.5 hover:border-brand-400/60 hover:shadow-panel focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-300 focus-visible:ring-offset-2 focus-visible:ring-offset-(--cb-surface-soft)"
                  >
                    <span className="block text-lg font-medium">{u.displayName}</span>
                    {u.email ? (
                      <span className="mt-1 block text-sm text-(--cb-muted)">
                        {u.email}
                      </span>
                    ) : null}
                  </button>
                </li>
              ))}
            </ul>

            {!hasUsers && (
              <p className="text-sm text-(--cb-muted)">
                Aucun utilisateur n’est disponible pour cette boutique.
              </p>
            )}
          </div>
        )}
      </main>
    </Page>
  )
}





