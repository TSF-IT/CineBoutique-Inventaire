import { useMemo } from 'react'
import { useShop } from '@/state/ShopContext'
import { loadSelectedUserForShop } from '@/lib/selectedUserStorage'

// On infère le type retourné par la fonction au lieu d'importer un type non exporté
type SelectedUser = ReturnType<typeof loadSelectedUserForShop>

function pickUserName(user: SelectedUser): string | null {
  if (!user) return null
  const rec = user as unknown as Record<string, unknown>
  const get = (k: string) => {
    const v = rec[k]
    return typeof v === 'string' && v.trim().length > 0 ? v : null
  }
  // tolérant: userName > displayName > name
  return get('userName') ?? get('displayName') ?? get('name')
}

function pickUserId(user: SelectedUser): string | null {
  if (!user) return null
  const rec = user as unknown as Record<string, unknown>
  const v = rec['userId']
  return typeof v === 'string' && v.length > 0 ? v : null
}

export function useSelectedShop() {
  const { shop } = useShop()
  return { shopId: shop?.id ?? null }
}

export function useSelectedUser() {
  const { shop } = useShop()
  const user = useMemo(
    () => (shop ? loadSelectedUserForShop(shop.id) : null),
    [shop] // règle exhaustive-deps
  )
  return {
    userId: pickUserId(user),
    userName: pickUserName(user),
  }
}
