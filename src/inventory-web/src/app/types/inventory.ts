import { z } from 'zod'

export type CountType = 1 | 2

export interface InventorySummary {
  activeCounts: number
  conflicts: number
}

export const LocationSchema = z.object({
  id: z.string().uuid(),
  code: z.string(),
  label: z.string(),
  description: z.string().nullable().optional(),
})

export const LocationsSchema = z.array(LocationSchema)

export type Location = z.infer<typeof LocationSchema>

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
