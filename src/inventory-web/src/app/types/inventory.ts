import { z } from 'zod'

export type CountType = 1 | 2 | 3

export interface InventorySummary {
  activeSessions: number
  openRuns: number
  lastActivityUtc: string | null
}

export const LocationSchema = z.object({
  id: z.string().uuid(),
  code: z.string(),
  label: z.string(),
  isBusy: z.boolean(),
  busyBy: z.string().nullable(),
  activeRunId: z.string().uuid().nullable().optional(),
  activeCountType: z
    .number()
    .int()
    .refine((value) => value === 1 || value === 2 || value === 3, {
      message: 'activeCountType doit être 1, 2 ou 3',
    })
    .nullable()
    .optional(),
  activeStartedAtUtc: z.string().datetime({ offset: true }).nullable().optional(),
})

export const LocationsSchema = z.array(LocationSchema)

export type Location = z.infer<typeof LocationSchema>

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
