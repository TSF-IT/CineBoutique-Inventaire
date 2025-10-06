import { useEffect, useMemo, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import http from '@/lib/api/http'
import { API_BASE } from '@/lib/api/config'
import { useShop } from '@/state/ShopContext'
import { saveSelectedUserForShop } from '@/lib/selectedUserStorage'
import { z } from 'zod'
import { Page } from '@/app/components/Page'
import { LoadingIndicator } from '@/app/components/LoadingIndicator'
import { Button } from '@/app/components/ui/Button'
import { useInventory } from '@/app/contexts/InventoryContext'
import { clearShop } from '@/lib/shopStorage'

type ShopUser = {
  id: string
  displayName: string
  email?: string | null
}

// Schéma tolérant pour l’API users, sans any
const ShopUserApiSchema = z
  .object({
    id: z
      .string()
      .trim()
      .min(1, 'Identifiant utilisateur manquant'),
    displayName: z
      .string()
      .min(1)
      .or(z.string().length(0).transform(() => 'Utilisateur')),
    email: z.string().email().nullable().optional(),
  })
  .passthrough()

const DEFAULT_ERROR_MESSAGE = "Impossible de charger les utilisateurs."

type RedirectState = {
  redirectTo?: string
} | null

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
    if (typeof target !== 'string') {
      return null
    }
    const normalized = target.trim()
    return normalized.length > 0 ? normalized : null
  }, [redirectState])

  useEffect(() => {
    if (!isLoaded) return
    if (!shop) {
      const options = redirectTo ? { replace: true, state: { redirectTo } } : { replace: true }
      navigate('/select-shop', options)
      return
    }

    const abort = new AbortController()
    setLoading(true)
    setErr(null) // nettoie une éventuelle erreur précédente

    http(`${API_BASE}/shops/${encodeURIComponent(shop.id)}/users`, { signal: abort.signal })
      .then((res) => (Array.isArray(res) ? res : []))
      .then((arr) => z.array(ShopUserApiSchema).parse(arr))
      .then((arr) =>
        arr.map<ShopUser>((u) => ({
          id: u.id,
          displayName: u.displayName || 'Utilisateur',
          email: u.email ?? null,
        })),
      )
      .then(setUsers)
      .catch((e: any) => {
        const rawMsg = String(e?.message || '')
        const status = typeof e?.status === 'number' ? e.status : undefined

        // 1) Aborts React dev: on ignore
        if (e?.name === 'AbortError' || rawMsg === 'ABORTED' || rawMsg.toLowerCase().includes('aborted')) {
          return
        }

        // 2) Si la boutique n'existe plus OU si le serveur est malade => on purge et on renvoie choisir
        const msg = rawMsg || 'Erreur de chargement'
        if (status === 404 || /introuvable/i.test(msg) || (status && status >= 500)) {
          console.error('[users] backend error', { status, msg, url: `${API_BASE}/shops/${shop?.id}/users` })
          clearShop()
          navigate('/select-shop', redirectTo ? { replace: true, state: { redirectTo } } : { replace: true })
          return
        }

        // 3) Sinon, erreur affichable
        setErr(msg)
      })
      .finally(() => setLoading(false))

    return () => abort.abort('route-change')
  }, [shop, isLoaded, navigate, redirectTo])


  const onPick = (u: ShopUser) => {
    if (!shop) return

    // Infère le type du second paramètre exigé par saveSelectedUserForShop
    type Snapshot = Parameters<typeof saveSelectedUserForShop>[1]

    const loginCandidate = u.email && u.email.trim().length > 0 ? u.email.trim() : u.displayName
    const snapshot: Snapshot = {
      id: u.id,
      displayName: u.displayName,
      shopId: shop.id,
      login: loginCandidate,
      isAdmin: false,
      disabled: false,
    }

    setSelectedUser(snapshot)
    saveSelectedUserForShop(shop.id, snapshot)
    navigate(redirectTo ?? '/', { replace: true })
  }

  if (!isLoaded) return null
  if (!shop) return null

  const shouldShowList = !loading && !err
  const hasUsers = users.length > 0

  return (
    <Page className="px-4 py-6 sm:px-6">
      <main className="flex flex-1 flex-col justify-center gap-8">
        <div>
          <h1 className="text-2xl font-semibold text-slate-900 dark:text-slate-100">Merci de vous identifier</h1>
        </div>

        {loading && <LoadingIndicator label="Chargement des utilisateurs…" />}

        {!loading && err && (
          <div
            className="rounded-2xl border border-red-200 bg-red-50 p-4 text-sm text-red-800 dark:border-red-900/60 dark:bg-red-950/40 dark:text-red-200"
            role="alert"
          >
            <p className="font-semibold">{err || DEFAULT_ERROR_MESSAGE}</p>
            <p className="mt-1">Vérifiez votre connexion puis réessayez.</p>
            <Button
              className="mt-4"
              variant="secondary"
              onClick={() => navigate('/select-shop', redirectTo ? { state: { redirectTo } } : undefined)}
            >
              Changer de boutique
            </Button>
          </div>
        )}

        {shouldShowList && (
          <div className="space-y-4">
            <ul className="grid gap-3 sm:grid-cols-2">
              {users.map((u) => (
                <li key={u.id}>
                  <button
                    type="button"
                    onClick={() => onPick(u)}
                    className="block w-full rounded-2xl border border-slate-300 bg-white px-5 py-4 text-left text-base text-slate-900 shadow-sm transition hover:border-brand-300 hover:shadow-md focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-300 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-100"
                  >
                    <span className="block text-lg font-medium">{u.displayName}</span>
                    {u.email ? (
                      <span className="mt-1 block text-sm text-slate-600 dark:text-slate-300">{u.email}</span>
                    ) : null}
                  </button>
                </li>
              ))}
            </ul>

            {!hasUsers && (
              <p className="text-sm text-slate-500 dark:text-slate-300">
                Aucun utilisateur n’est disponible pour cette boutique.
              </p>
            )}

            <div>
              <Button
                variant="secondary"
                onClick={() => navigate('/select-shop', redirectTo ? { state: { redirectTo } } : undefined)}
              >
                Changer de boutique
              </Button>
            </div>
          </div>
        )}
      </main>
    </Page>
  )
}
