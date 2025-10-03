import type { Shop } from '@/types/shop'

export const SHOP_STORAGE_KEY = 'cb.shop'

const isShop = (value: unknown): value is Shop =>
  typeof value === 'object' &&
  value !== null &&
  'id' in value &&
  typeof (value as { id: unknown }).id === 'string' &&
  'name' in value &&
  typeof (value as { name: unknown }).name === 'string'

export function saveShop(shop: Shop) {
  localStorage.setItem(SHOP_STORAGE_KEY, JSON.stringify(shop))
}

export function loadShop(): Shop | null {
  const raw = localStorage.getItem(SHOP_STORAGE_KEY)
  if (!raw) {
    return null
  }

  try {
    const parsed = JSON.parse(raw)
    if (isShop(parsed)) {
      return parsed
    }
  } catch {
    // Ignored: will clear below
  }

  localStorage.removeItem(SHOP_STORAGE_KEY)
  return null
}

export function clearShop() {
  localStorage.removeItem(SHOP_STORAGE_KEY)
}
