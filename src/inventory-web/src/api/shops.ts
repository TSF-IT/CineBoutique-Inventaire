import { z } from 'zod'
import http from '@/lib/api/http'
import { API_BASE } from '@/lib/api/config'
import type { Shop } from '@/types/shop'

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
