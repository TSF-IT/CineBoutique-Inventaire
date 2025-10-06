import { z } from 'zod'

export enum CountType {
  Count1 = 1,
  Count2 = 2,
  Count3 = 3,
}

export interface InventorySummary {
  activeSessions: number
  openRuns: number
  completedRuns: number
  conflicts: number
  lastActivityUtc: string | null
  openRunDetails: OpenRunSummary[]
  completedRunDetails: CompletedRunSummary[]
  conflictZones: ConflictZoneSummary[]
}

export interface OpenRunSummary {
  runId: string
  locationId: string
  locationCode: string
  locationLabel: string
  countType: CountType
  ownerDisplayName: string | null
  ownerUserId: string | null
  startedAtUtc: string
}

export interface CompletedRunSummary {
  runId: string
  locationId: string
  locationCode: string
  locationLabel: string
  countType: CountType
  ownerDisplayName: string | null
  ownerUserId: string | null
  startedAtUtc: string
  completedAtUtc: string
}

export interface CompletedRunDetailItem {
  productId: string
  sku: string
  name: string
  ean: string | null
  quantity: number
}

export interface CompletedRunDetail {
  runId: string
  locationId: string
  locationCode: string
  locationLabel: string
  countType: CountType
  ownerDisplayName: string | null
  ownerUserId: string | null
  startedAtUtc: string
  completedAtUtc: string
  items: CompletedRunDetailItem[]
}

export interface ConflictZoneSummary {
  locationId: string
  locationCode: string
  locationLabel: string
  conflictLines: number
}

export interface ConflictZoneItem {
  ean: string
  productId: string
  qtyC1: number
  qtyC2: number
  delta: number
}

export interface ConflictZoneDetail {
  locationId: string
  locationCode: string
  locationLabel: string
  items: ConflictZoneItem[]
}

const CountTypeSchema = z.number().int().min(1).max(3)

const zDateOrNull = z
  .preprocess((value) => {
    if (value === null || value === undefined) {
      return null
    }

    if (value instanceof Date) {
      return isNaN(value.getTime()) ? null : value
    }

    const date = new Date(value as string | number | Date)
    return isNaN(date.getTime()) ? null : date
  }, z.date().nullable())
  .transform((value) => (value instanceof Date ? value : null))

export const LocationSchema = z.object({
  id: z.string().uuid(),
  code: z.string().min(1),
  label: z.string().min(1),
  isBusy: z.boolean(),
  busyBy: z.string().nullish().transform((value) => (typeof value === 'string' ? value : null)),
  activeRunId: z.string().uuid().nullish().transform((value) => value ?? null),
  activeCountType: CountTypeSchema.nullish().transform((value) => (typeof value === 'number' ? value : null)),
  activeStartedAtUtc: zDateOrNull,
  countStatuses: z
    .array(
      z.object({
        countType: CountTypeSchema,
        status: z.enum(['not_started', 'in_progress', 'completed']),
        runId: z.string().uuid().nullish().transform((value) => value ?? null),
        ownerDisplayName: z
          .string()
          .nullish()
          .transform((value) => (typeof value === 'string' ? value : null)),
        ownerUserId: z.string().uuid().nullish().transform((value) => value ?? null),
        startedAtUtc: zDateOrNull,
        completedAtUtc: zDateOrNull,
      }),
    )
    .default([]),
})

export const LocationsSchema = z.array(LocationSchema)

export type Location = z.infer<typeof LocationSchema>

export type LocationCountStatus = Location['countStatuses'][number]

export interface Product {
  ean: string
  name: string
  stock?: number
  lastCountedAt?: string | null
}

export interface InventoryItem {
  id: string
  product: Product
  quantity: number
  lastScanAt: string
  isManual: boolean
  addedAt: number
}

export type InventoryLogEventType =
  | 'status'
  | 'item-added'
  | 'item-incremented'
  | 'item-quantity-updated'
  | 'item-removed'

export interface InventoryLogEntryContext {
  ean?: string
  productName?: string
  quantity?: number
  isManual?: boolean
}

export interface InventoryLogEntry {
  id: string
  timestamp: string
  type: InventoryLogEventType
  message: string
  context?: InventoryLogEntryContext
}

export interface InventoryCountSubmissionItem {
  ean: string
  quantity: number
  isManual: boolean
}

export interface CompleteInventoryRunPayload {
  runId?: string | null
  countType: CountType
  ownerUserId: string
  items: InventoryCountSubmissionItem[]
}

export interface CompleteInventoryRunResult {
  runId: string
  inventorySessionId: string
  locationId: string
  countType: CountType
  completedAtUtc: string
  itemsCount: number
  totalQuantity: number
}
