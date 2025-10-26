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

type StoredSelectedUserSnapshotV3 = {
  userId: string
  displayName?: string | null
  login?: string | null
  shopId?: string | null
  isAdmin?: boolean
  disabled?: boolean
}

export type StoredSelectedUserSnapshot =
  | StoredSelectedUserSnapshotV3
  | StoredSelectedUserSnapshotV2
  | StoredSelectedUserSnapshotLegacy

const sanitizeString = (value: unknown): string => (typeof value === 'string' ? value : String(value ?? '')).trim()

const isStoredSelectedUserSnapshot = (value: unknown): value is StoredSelectedUserSnapshot => {
  if (typeof value !== 'object' || value === null) {
    return false
  }

  const record = value as Record<string, unknown>

  if ('userId' in record && typeof record.userId === 'string' && record.userId.trim().length > 0) {
    return true
  }

  if ('id' in record && typeof record.id === 'string' && record.id.trim().length > 0) {
    return true
  }

  return false
}

const toStoredSnapshot = (user: ShopUser): StoredSelectedUserSnapshotV3 => ({
  userId: sanitizeString(user.id),
  displayName: sanitizeString(user.displayName || user.login || user.id) || null,
  login: sanitizeString(user.login || user.id) || null,
  shopId: sanitizeString(user.shopId) || null,
  isAdmin: Boolean(user.isAdmin),
  disabled: Boolean(user.disabled),
})

export const toShopUserFromSnapshot = (
  snapshot: StoredSelectedUserSnapshot | null,
  fallbackShopId: string,
): ShopUser | null => {
  if (!snapshot) {
    return null
  }

  if ('userId' in snapshot && snapshot.userId.trim().length > 0) {
    const loginCandidate = 'login' in snapshot ? sanitizeString(snapshot.login) : ''
    const displayNameCandidate = 'displayName' in snapshot ? sanitizeString(snapshot.displayName) : ''
    const shopIdCandidate = 'shopId' in snapshot ? sanitizeString(snapshot.shopId) : ''

    if (loginCandidate && displayNameCandidate) {
      return {
        id: snapshot.userId.trim(),
        login: loginCandidate || snapshot.userId.trim(),
        displayName: displayNameCandidate || loginCandidate || snapshot.userId.trim(),
        shopId: shopIdCandidate || fallbackShopId,
        isAdmin: 'isAdmin' in snapshot ? Boolean(snapshot.isAdmin) : false,
        disabled: 'disabled' in snapshot ? Boolean(snapshot.disabled) : false,
      }
    }

    if (displayNameCandidate || loginCandidate) {
      const value = displayNameCandidate || loginCandidate
      return {
        id: snapshot.userId.trim(),
        login: loginCandidate || value || snapshot.userId.trim(),
        displayName: value || snapshot.userId.trim(),
        shopId: shopIdCandidate || fallbackShopId,
        isAdmin: 'isAdmin' in snapshot ? Boolean(snapshot.isAdmin) : false,
        disabled: 'disabled' in snapshot ? Boolean(snapshot.disabled) : false,
      }
    }

    return {
      id: snapshot.userId.trim(),
      login: snapshot.userId.trim(),
      displayName: snapshot.userId.trim(),
      shopId: shopIdCandidate || fallbackShopId,
      isAdmin: 'isAdmin' in snapshot ? Boolean(snapshot.isAdmin) : false,
      disabled: 'disabled' in snapshot ? Boolean(snapshot.disabled) : false,
    }
  }

  if ('id' in snapshot && typeof snapshot.id === 'string' && snapshot.id.trim().length > 0) {
    const normalizedId = snapshot.id.trim()
    return {
      id: normalizedId,
      login: normalizedId,
      displayName: normalizedId,
      shopId: fallbackShopId,
      isAdmin: false,
      disabled: false,
    }
  }

  return null
}

export const saveSelectedUserForShop = (shopId: string, user: ShopUser) => {
  const storage = getSessionStorage()
  if (!storage) {
    return
  }

  try {
    storage.setItem(buildStorageKey(shopId), JSON.stringify(toStoredSnapshot(user)))
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

export const loadSelectedShopUser = (shopId: string): ShopUser | null => {
  const snapshot = loadSelectedUserForShop(shopId)
  if (!snapshot) {
    return null
  }

  const fallbackShopId = sanitizeString(shopId) || shopId
  return toShopUserFromSnapshot(snapshot, fallbackShopId)
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
