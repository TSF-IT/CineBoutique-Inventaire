import { z } from 'zod'

export enum CountType {
  Count1 = 1,
  Count2 = 2,
  Count3 = 3,
}

export interface InventorySummary {
  activeSessions: number
  openRuns: number
  conflicts: number
  lastActivityUtc: string | null
  openRunDetails: OpenRunSummary[]
  conflictDetails: ConflictSummary[]
}

export interface OpenRunSummary {
  runId: string
  locationId: string
  locationCode: string
  locationLabel: string
  countType: CountType
  operatorDisplayName: string | null
  startedAtUtc: string
}

export interface ConflictSummary {
  conflictId: string
  countLineId: string
  countingRunId: string
  locationId: string
  locationCode: string
  locationLabel: string
  countType: CountType
  operatorDisplayName: string | null
  createdAtUtc: string
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
  countStatuses: z
    .array(
      z.object({
        countType: z.number().int(),
        status: z.enum(['not_started', 'in_progress', 'completed']),
        runId: z.string().uuid().nullable(),
        operatorDisplayName: z.string().nullable(),
        startedAtUtc: IsoDateNullable,
        completedAtUtc: IsoDateNullable,
      }),
    )
    .default([]),
})

export const LocationSchema = LocationDto

export const LocationsSchema = z.array(LocationDto)

export type Location = z.infer<typeof LocationDto>

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
  operator: string
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
