import { z } from 'zod'

export enum CountType {
  Count1 = 1,
  Count2 = 2,
  Count3 = 3,
}

export interface InventorySummary {
  activeSessions: number
  openRuns: number
  lastActivityUtc: string | null
}

const IsoDateNullable = z.preprocess((value) => {
  if (value == null || value === '') {
    return null
  }
  if (value instanceof Date) {
    return value
  }
  if (typeof value === 'string') {
    const timestamp = Date.parse(value)
    return Number.isNaN(timestamp) ? value : new Date(timestamp)
  }
  return value
}, z.date().nullable())

export const LocationDto = z.object({
  id: z.string().uuid(),
  code: z.string(),
  label: z.string(),
  isBusy: z.boolean(),
  busyBy: z.string().nullable(),
  activeRunId: z.string().uuid().nullable(),
  activeCountType: z.number().int().nullable(),
  activeStartedAtUtc: IsoDateNullable,
})

export const LocationSchema = LocationDto

export const LocationsSchema = z.array(LocationDto)

export type Location = z.infer<typeof LocationDto>

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
