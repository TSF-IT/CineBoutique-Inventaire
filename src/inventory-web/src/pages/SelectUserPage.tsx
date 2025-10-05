import { useEffect, useState } from 'react'
import { appStore, OperatorCtx } from '../lib/context/appContext'
import { useNavigate } from 'react-router-dom'
import { fetchShopUsers } from '../lib/api/shops' // assume exists

type UserDto = { id: string; displayName: string; isAdmin: boolean }

export default function SelectUserPage() {
  const nav = useNavigate()
  const shop = appStore.getShop()
  const [users, setUsers] = useState<UserDto[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let mounted = true
    if (!shop) return
    setLoading(true)
    fetchShopUsers(shop.id)
      .then((x) => {
        if (mounted) setUsers(x)
      })
      .finally(() => mounted && setLoading(false))
    return () => {
      mounted = false
    }
  }, [shop?.id])

  if (!shop) return null

  const onPick = (u: UserDto) => {
    const op: OperatorCtx = { id: u.id, name: u.displayName }
    appStore.setOperator(op)
    nav('/home', { replace: true })
  }

  return (
    <main className="mx-auto w-full max-w-[1100px] px-3 sm:px-4 lg:px-6">
      <h1 className="mt-4 mb-2 text-[clamp(18px,4vw,24px)] font-semibold">Choisir un utilisateur</h1>
      <p className="mb-3 text-[clamp(13px,3vw,16px)] text-muted-foreground">Boutique&nbsp;: {shop.name}</p>

      {loading ? (
        <div className="grid grid-cols-2 gap-2 sm:gap-3 [@media(orientation:landscape)]:grid-cols-3 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5">
          {Array.from({ length: 6 }).map((_, index) => (
            <div key={index} className="h-11 animate-pulse rounded-xl bg-gray-200/50" />
          ))}
        </div>
      ) : users.length === 0 ? (
        <div className="text-sm text-muted-foreground">Aucun utilisateur actif pour cette boutique.</div>
      ) : (
        <ul className="grid grid-cols-2 gap-2 sm:gap-3 [@media(orientation:landscape)]:grid-cols-3 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5">
          {users
            .sort(
              (left, right) =>
                Number(right.isAdmin) - Number(left.isAdmin) ||
                left.displayName.localeCompare(right.displayName, undefined, { sensitivity: 'accent' }),
            )
            .map((user) => (
              <li key={user.id}>
                <button
                  type="button"
                  onClick={() => onPick(user)}
                  aria-label={`Sélectionner ${user.displayName}`}
                  className="h-11 w-full min-w-[120px] rounded-xl border px-3 text-left text-[clamp(13px,3vw,16px)] hover:bg-gray-50 focus:outline focus:outline-2 focus:outline-offset-1"
                >
                  {user.isAdmin ? '⭐ ' : ''}
                  {user.displayName}
                </button>
              </li>
            ))}
        </ul>
      )}
    </main>
  )
}
