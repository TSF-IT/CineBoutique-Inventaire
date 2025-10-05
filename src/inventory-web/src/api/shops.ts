import { z } from 'zod'
import http from '@/lib/api/http'
import { API_BASE } from '@/lib/api/config'
import type { Shop } from '@/types/shop'
import type { ShopUser } from '@/types/user'

const ShopSchema = z.object({
  id: z.string().min(1),
  name: z.string().min(1),
})

const ShopsSchema = z.array(ShopSchema)

export const fetchShops = async (signal?: AbortSignal): Promise<Shop[]> => {
  const data = await http(`${API_BASE}/shops`, { signal })
  const parsed = ShopsSchema.safeParse(data)
  if (!parsed.success) {
    throw new Error('Format de boutiques invalide')
  }
  return parsed.data
}

const ShopUserSchema = z.object({
  id: z.string().min(1),
  shopId: z.string().min(1),
  login: z.string().min(1),
  displayName: z.string().min(1),
  isAdmin: z.boolean(),
  disabled: z.boolean().default(false),
})

const ShopUsersSchema = z.array(ShopUserSchema)

export const fetchShopUsers = async (shopId: string, signal?: AbortSignal): Promise<ShopUser[]> => {
  const data = await http(`${API_BASE}/shops/${shopId}/users`, { signal })
  const parsed = ShopUsersSchema.safeParse(data)
  if (!parsed.success) {
    throw new Error('Format de la liste des utilisateurs invalide')
  }
  return parsed.data
}
