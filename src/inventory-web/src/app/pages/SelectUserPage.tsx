import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import http from '@/lib/api/http'
import { API_BASE } from '@/lib/api/config'
import { useShop } from '@/state/ShopContext'
import { saveSelectedUserForShop } from '@/lib/selectedUserStorage'
import { z } from 'zod'

type ShopUser = {
  id: string
  displayName: string
  email?: string | null
}

// Schéma tolérant pour l’API users, sans any
const ShopUserApiSchema = z
  .object({
    id: z.string().uuid(),
    displayName: z
      .string()
      .min(1)
      .or(z.string().length(0).transform(() => 'Utilisateur')),
    email: z.string().email().nullable().optional(),
  })
  .passthrough()

export default function SelectUserPage() {
  const navigate = useNavigate()
  const { shop, isLoaded } = useShop()
  const [users, setUsers] = useState<ShopUser[]>([])
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    if (!isLoaded) return
    if (!shop) {
      navigate('/select-shop', { replace: true })
      return
    }

    const abort = new AbortController()
    setLoading(true)

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
      .catch((e) => setErr(e?.message ?? 'Erreur de chargement'))
      .finally(() => setLoading(false))

    return () => abort.abort()
  }, [shop, isLoaded, navigate])

const onPick = (u: ShopUser) => {
  if (!shop) return

  // Infère le type du second paramètre exigé par saveSelectedUserForShop
  type Snapshot = Parameters<typeof saveSelectedUserForShop>[1]

  const snapshot: Snapshot = {
    id: u.id,
    displayName: u.displayName,
    shopId: shop.id,
    // on propose un login simple: email si dispo sinon le displayName
    login: (u.email && u.email.trim().length > 0) ? u.email : u.displayName,
    isAdmin: false,
    disabled: false,
  }

  saveSelectedUserForShop(shop.id, snapshot)
  navigate('/', { replace: true })
}

  if (!isLoaded) return null
  if (!shop) return null
  if (loading) return <div className="p-4">Chargement des utilisateurs…</div>
  if (err) {
    return (
      <div className="p-4 text-red-600">
        {err}{' '}
        <button type="button" className="underline" onClick={() => navigate('/select-shop')}>
          Changer de boutique
        </button>
      </div>
    )
  }

  return (
    <div className="p-4 max-w-md mx-auto">
      <h1 className="text-xl font-semibold mb-4">Choisir l’utilisateur</h1>
      <ul className="space-y-2">
        {users.map((u) => (
          <li key={u.id}>
            <button
              type="button"
              className="w-full rounded-2xl shadow p-3 text-left hover:shadow-md"
              onClick={() => onPick(u)}
            >
              <div className="font-medium">{u.displayName}</div>
              {u.email ? <div className="text-sm opacity-70">{u.email}</div> : null}
            </button>
          </li>
        ))}
      </ul>
      <div className="mt-6 text-sm">
        <button type="button" className="underline" onClick={() => navigate('/select-shop')}>
          Changer de boutique
        </button>
      </div>
    </div>
  )
}
