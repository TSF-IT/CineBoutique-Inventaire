import { z } from 'zod'

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

export const CountStatusItemSchema = z.object({
  runId: z.string().uuid().nullish().transform((v) => v ?? null),
  startedAtUtc: zDateOrNull,
  completedAtUtc: zDateOrNull,
})

export const LocationSummarySchema = z.object({
  locationId: z.string().uuid(),
  locationName: z.string(),
  busyBy: z.string().nullish().transform((v) => v ?? null),
  activeRunId: z.string().uuid().nullish().transform((v) => v ?? null),
  activeCountType: z.number().int().nullish().transform((v) => (typeof v === 'number' ? v : null)),
  activeStartedAtUtc: zDateOrNull,
  countStatuses: z.array(CountStatusItemSchema).default([]),
})

export const LocationSummaryListSchema = z.array(LocationSummarySchema)
