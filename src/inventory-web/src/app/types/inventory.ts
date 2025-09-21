export type CountType = 1 | 2

export interface InventorySummary {
  activeCounts: number
  conflicts: number
}

export interface Location {
  id: string
  name: string
  description?: string | null
}

export interface InventoryCheckResponse {
  hasActive: boolean
  owner?: string
  sessionId?: string
}

export interface Product {
  ean: string
  name: string
  stock?: number
  lastCountedAt?: string | null
}

export interface InventoryItem {
  product: Product
  quantity: number
  lastScanAt: string
  isManual: boolean
}

export interface ManualProductInput {
  ean: string
  name: string
}
