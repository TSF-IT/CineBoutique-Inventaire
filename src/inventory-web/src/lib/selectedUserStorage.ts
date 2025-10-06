import type { ShopUser } from '@/types/user'

export const SELECTED_USER_STORAGE_PREFIX = 'cb.inventory.selectedUser'

const buildStorageKey = (shopId: string) => `${SELECTED_USER_STORAGE_PREFIX}.${shopId}`

const getSessionStorage = (): Storage | null => {
  if (typeof window === 'undefined') {
    return null
  }

  try {
    return window.sessionStorage
  } catch (error) {
    if (import.meta.env.DEV) {
      console.warn('[selectedUserStorage] SessionStorage inaccessible', error)
    }
    return null
  }
}

type StoredSelectedUserSnapshotV2 = {
  userId: string
}

type StoredSelectedUserSnapshotLegacy = {
  id: string
  userId?: never
}

export type StoredSelectedUserSnapshot =
  | StoredSelectedUserSnapshotV2
  | StoredSelectedUserSnapshotLegacy

const isStoredSelectedUserSnapshot = (value: unknown): value is StoredSelectedUserSnapshot =>
  typeof value === 'object' &&
  value !== null &&
  ((
    'userId' in value &&
    typeof (value as { userId: unknown }).userId === 'string' &&
    (value as { userId: string }).userId.trim().length > 0
  ) ||
    ('id' in value && typeof (value as { id: unknown }).id === 'string' && (value as { id: string }).id.trim().length > 0))

export const saveSelectedUserForShop = (shopId: string, user: ShopUser) => {
  const storage = getSessionStorage()
  if (!storage) {
    return
  }

  try {
    storage.setItem(buildStorageKey(shopId), JSON.stringify({ userId: user.id }))
  } catch (error) {
    if (import.meta.env.DEV) {
      console.warn('[selectedUserStorage] Impossible de sauvegarder le propriétaire sélectionné', error)
    }
  }
}

export const loadSelectedUserForShop = (shopId: string): StoredSelectedUserSnapshot | null => {
  const storage = getSessionStorage()
  if (!storage) {
    return null
  }

  try {
    const raw = storage.getItem(buildStorageKey(shopId))
    if (!raw) {
      return null
    }

    const parsed = JSON.parse(raw)
    if (isStoredSelectedUserSnapshot(parsed)) {
      return parsed
    }
  } catch (error) {
    if (import.meta.env.DEV) {
      console.warn('[selectedUserStorage] Impossible de charger le propriétaire sélectionné', error)
    }
  }

  return null
}

export const clearSelectedUserForShop = (shopId: string) => {
  const storage = getSessionStorage()
  if (!storage) {
    return
  }

  try {
    storage.removeItem(buildStorageKey(shopId))
  } catch (error) {
    if (import.meta.env.DEV) {
      console.warn('[selectedUserStorage] Impossible de nettoyer le propriétaire sélectionné', error)
    }
  }
}
