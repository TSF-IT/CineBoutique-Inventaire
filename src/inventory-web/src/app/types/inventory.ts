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

export const LocationSchema = z.object({
  id: z.string().uuid(),
  code: z.string().min(1),
  label: z.string().min(1),
  isBusy: z.boolean(),
  busyBy: z.string().nullable(),
  activeRunId: z.string().uuid().nullable(),
  activeCountType: CountTypeSchema.nullable(),
  activeStartedAtUtc: z.coerce.date().nullable(),
  countStatuses: z.array(
    z.object({
      countType: CountTypeSchema,
      status: z.enum(['not_started', 'in_progress', 'completed']),
      runId: z.string().uuid().nullable(),
      ownerDisplayName: z.string().nullable(),
      ownerUserId: z.string().uuid().nullable(),
      startedAtUtc: z.coerce.date().nullable(),
      completedAtUtc: z.coerce.date().nullable(),
    }),
  ),
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
