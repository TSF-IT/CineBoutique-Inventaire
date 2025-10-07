import type { Location } from '../types/inventory'
import type { ShopUser } from '../types/user'
import http from '@/lib/api/http'
import { API_BASE } from '@/lib/api/config'

interface LocationPayload {
  label: string
  code?: string
  description?: string
}

export const createLocation = async (payload: LocationPayload): Promise<Location> => {
  const data = await http(`${API_BASE}/locations`, {
    method: 'POST',
    body: JSON.stringify(payload),
  })
  return data as Location
}

export const updateLocation = async (
  id: string,
  payload: Partial<LocationPayload>,
): Promise<Location> => {
  const data = await http(`${API_BASE}/locations/${id}`, {
    method: 'PUT',
    body: JSON.stringify(payload),
  })
  return data as Location
}

const sanitizeIdentifier = (value: string): string => value.trim()

const buildShopUsersUrl = (shopId: string) => {
  const trimmed = sanitizeIdentifier(shopId)
  if (!trimmed) {
    throw new Error("L'identifiant boutique est requis pour gérer les utilisateurs.")
  }
  return `${API_BASE}/shops/${encodeURIComponent(trimmed)}/users`
}

type ShopUserPayload = {
  login: string
  displayName: string
  isAdmin: boolean
}

type ShopUserUpdatePayload = ShopUserPayload & { id: string }

export const createShopUser = async (shopId: string, payload: ShopUserPayload): Promise<ShopUser> => {
  const data = await http(buildShopUsersUrl(shopId), {
    method: 'POST',
    body: payload,
  })
  return data as ShopUser
}

export const updateShopUser = async (
  shopId: string,
  payload: ShopUserUpdatePayload,
): Promise<ShopUser> => {
  const data = await http(buildShopUsersUrl(shopId), {
    method: 'PUT',
    body: payload,
  })
  return data as ShopUser
}

export const disableShopUser = async (shopId: string, id: string): Promise<ShopUser> => {
  const data = await http(buildShopUsersUrl(shopId), {
    method: 'DELETE',
    body: { id },
  })
  return data as ShopUser
}

