import { useEffect, useMemo, useRef, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { Button } from '../../components/ui/Button'
import { Input } from '../../components/ui/Input'
import { Card } from '../../components/Card'
import { useInventory } from '../../contexts/InventoryContext'
import { fetchShopUsers } from '../../api/shopUsers'
import type { ShopUser } from '@/types/user'
import { useShop } from '@/state/ShopContext'
import { clearSelectedUserForShop, loadSelectedUserForShop, saveSelectedUserForShop } from '@/lib/selectedUserStorage'

export const InventoryUserStep = () => {
  const navigate = useNavigate()
  const location = useLocation()
  const { selectedUser, setSelectedUser, reset } = useInventory()
  const { shop, setShop } = useShop()
  const [search, setSearch] = useState('')
  const [users, setUsers] = useState<ShopUser[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const redirectTo = useMemo(() => {
    const state = location.state as { redirectTo?: string } | null
    if (!state || typeof state.redirectTo !== 'string') {
      return null
    }
    const target = state.redirectTo.trim()
    return target.length > 0 ? target : null
  }, [location.state])

  const resetRef = useRef(reset)

  useEffect(() => {
    resetRef.current = reset
  }, [reset])

  useEffect(() => {
    let isCancelled = false

    const loadUsers = async () => {
      if (!shop?.id) {
        setUsers([])
        setLoading(false)
        setError('Aucune boutique sélectionnée.')
        return
      }

      setLoading(true)
      setError(null)
      try {
        const data = await fetchShopUsers(shop.id)
        if (!isCancelled) {
          setUsers(data.filter((user) => !user.disabled))
        }
      } catch (err) {
        if (isCancelled) {
          return
        }

        const error = err as { __shopNotFound?: boolean }

        if (
          error &&
          typeof error === 'object' &&
          '__shopNotFound' in error &&
          (error as { __shopNotFound?: boolean }).__shopNotFound
        ) {
          if (shop?.id) {
            clearSelectedUserForShop(shop.id)
          }
          resetRef.current?.()
          setShop(null)
          alert("La boutique enregistrée n'existe plus. Merci de la re-sélectionner.")
          navigate('/select-shop', { replace: true })
          return
        }

        if (import.meta.env.DEV) {
          console.error('Impossible de charger les utilisateurs de la boutique.', err)
        }
        setUsers([])
        setError('Impossible de charger la liste des utilisateurs. Réessayez plus tard.')
      } finally {
        if (!isCancelled) {
          setLoading(false)
        }
      }
    }

    void loadUsers()

    return () => {
      isCancelled = true
    }
  }, [shop?.id])

  useEffect(() => {
    if (!shop?.id) {
      return
    }

    if (!selectedUser) {
      clearSelectedUserForShop(shop.id)
      return
    }

    saveSelectedUserForShop(shop.id, selectedUser)
  }, [selectedUser, shop?.id])

  useEffect(() => {
    if (!shop?.id || loading) {
      return
    }

    const stored = loadSelectedUserForShop(shop.id)
    if (!stored) {
      return
    }

    const candidate = users.find((user) => user.id === stored.userId)
    if (!candidate) {
      clearSelectedUserForShop(shop.id)
      return
    }

    if (selectedUser?.id === candidate.id) {
      return
    }

    setSelectedUser(candidate)
  }, [loading, selectedUser?.id, setSelectedUser, shop?.id, users])

  const sortedUsers = useMemo(() => {
    return [...users].sort((left, right) =>
      left.displayName.localeCompare(right.displayName, undefined, { sensitivity: 'accent' }),
    )
  }, [users])

  const filteredUsers = useMemo(() => {
    const normalizedQuery = search.trim().toLowerCase()
    if (!normalizedQuery) {
      return sortedUsers
    }
    return sortedUsers.filter((user) => user.displayName.toLowerCase().includes(normalizedQuery))
  }, [search, sortedUsers])

  const handleSelect = (user: ShopUser) => {
    if (user.id !== selectedUser?.id) {
      setSelectedUser(user)
    }
    if (shop?.id) {
      saveSelectedUserForShop(shop.id, user)
    }
    if (redirectTo) {
      navigate(redirectTo, { replace: true })
      return
    }
    navigate('/inventory/location')
  }

  const handleContinue = () => {
    if (redirectTo) {
      navigate(redirectTo, { replace: true })
      return
    }
    navigate('/inventory/location')
  }

  return (
    <div className="flex flex-col gap-6">
      <Card className="flex flex-col gap-4">
        <h2 className="text-2xl font-semibold text-slate-900 dark:text-white">Qui réalise le comptage ?</h2>
        <p className="text-sm text-slate-500 dark:text-slate-400">
          Sélectionnez votre profil pour assurer la traçabilité des comptages.
        </p>
        <Input
          label="Rechercher"
          name="ownerQuery"
          placeholder="Rechercher un utilisateur"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
        />
        {error && <p className="text-sm text-red-600 dark:text-red-400">{error}</p>}
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
          {loading && filteredUsers.length === 0 ? (
            <p className="col-span-full text-sm text-slate-500 dark:text-slate-400">Chargement…</p>
          ) : filteredUsers.length === 0 ? (
            <p className="col-span-full text-sm text-slate-500 dark:text-slate-400">
              Aucun utilisateur ne correspond à votre recherche.
            </p>
          ) : (
            filteredUsers.map((user) => {
              const isSelected = selectedUser?.id === user.id
              return (
                <button
                  key={user.id}
                  type="button"
                  onClick={() => handleSelect(user)}
                  className={`rounded-2xl border px-4 py-4 text-center text-sm font-semibold transition-all ${
                    isSelected
                      ? 'border-brand-400 bg-brand-100 text-brand-700 dark:bg-brand-500/20 dark:text-brand-100'
                      : 'border-slate-200 bg-white text-slate-700 hover:border-brand-400/40 hover:bg-brand-50 dark:border-slate-700 dark:bg-slate-900/40 dark:text-slate-200'
                  }`}
                >
                  {user.displayName}
                </button>
              )
            })
          )}
        </div>
      </Card>
      {selectedUser && (
        <Button fullWidth className="py-4" onClick={handleContinue}>
          Continuer
        </Button>
      )}
    </div>
  )
}
