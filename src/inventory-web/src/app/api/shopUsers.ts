import type { ShopUser } from '@/types/user'
import http from '@/lib/api/http'
import { API_BASE } from '@/lib/api/config'

const sanitizeString = (value: unknown): string => (typeof value === 'string' ? value : String(value ?? '')).trim()

export const fetchShopUsers = async (shopId: string): Promise<ShopUser[]> => {
  const sanitizedShopId = shopId.trim()
  if (!sanitizedShopId) {
    return []
  }

  let response: unknown

  try {
    response = (await http(`${API_BASE}/shops/${encodeURIComponent(sanitizedShopId)}/users`)) as unknown
  } catch (error) {
    if (error && typeof error === 'object' && 'status' in error && (error as { status?: number }).status === 404) {
      ;(error as { __shopNotFound?: boolean }).__shopNotFound = true
    }
    throw error
  }

  if (!Array.isArray(response)) {
    return []
  }

  const users: ShopUser[] = []
  for (const entry of response) {
    if (!entry || typeof entry !== 'object') {
      continue
    }

    const record = entry as Record<string, unknown>
    const id = sanitizeString(record.id)
    const displayName = sanitizeString(record.displayName)
    if (!id || !displayName) {
      continue
    }

    const login = sanitizeString(record.login)
    const entryShopId = sanitizeString(record.shopId) || sanitizedShopId
    const isAdmin = Boolean(record.isAdmin)
    const disabled = Boolean(record.disabled)

    users.push({
      id,
      shopId: entryShopId,
      login: login || id,
      displayName,
      isAdmin,
      disabled,
    })
  }

  return users
}
