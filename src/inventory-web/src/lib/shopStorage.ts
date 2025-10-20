import type { Shop, ShopKind } from '@/types/shop'

export const SHOP_STORAGE_KEY = 'cb.shop'

const SHOP_KINDS: readonly ShopKind[] = ['boutique', 'lumiere', 'camera'] as const

const isShop = (value: unknown): value is Shop =>
  typeof value === 'object' &&
  value !== null &&
  'id' in value &&
  typeof (value as { id: unknown }).id === 'string' &&
  'name' in value &&
  typeof (value as { name: unknown }).name === 'string' &&
  'kind' in value &&
  typeof (value as { kind: unknown }).kind === 'string' &&
  SHOP_KINDS.includes((value as { kind: ShopKind }).kind)

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
