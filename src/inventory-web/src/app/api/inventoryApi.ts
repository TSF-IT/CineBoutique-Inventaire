import { CountType, LocationsSchema } from '../types/inventory'
import { LocationSummaryListSchema, type LocationSummaryList } from '@/types/summary'
import type { CompletedRunDetail, CompletedRunSummary, OpenRunSummary } from '../types/inventory'
import type {
  ConflictZoneDetail,
  ConflictZoneSummary,
  InventorySummary,
  Location,
  Product,
} from '../types/inventory'
import http, { HttpError } from '@/lib/api/http'
import { API_BASE } from '@/lib/api/config'
import { z } from 'zod'

/* ------------------------------ HTTP helpers ------------------------------ */

const isHttpError = (value: unknown): value is HttpError =>
  typeof value === 'object' &&
  value !== null &&
  typeof (value as { status?: unknown }).status === 'number' &&
  typeof (value as { url?: unknown }).url === 'string'

const tryExtractMessage = (value: unknown): string | null => {
  if (typeof value === 'string') {
    const trimmed = value.trim()
    return trimmed.length > 0 ? trimmed : null
  }
  return null
}

const extractProblemMessage = (problem: unknown): string | null => {
  if (!problem) return null
  if (typeof problem === 'string') return tryExtractMessage(problem)

  if (Array.isArray(problem)) {
    for (const item of problem) {
      const message = extractProblemMessage(item)
      if (message) return message
    }
    return null
  }

  if (typeof problem !== 'object') return null

  const record = problem as Record<string, unknown>
  const candidateKeys = ['message', 'detail', 'title', 'error', 'error_description'] as const
  for (const key of candidateKeys) {
    const message = tryExtractMessage(record[key])
    if (message) return message
  }

  const errors = (record as { errors?: unknown }).errors
  if (Array.isArray(errors)) {
    for (const item of errors) {
      const message = extractProblemMessage(item)
      if (message) return message
    }
  }

  return null
}

const getHttpErrorMessage = (error: HttpError): string | null => {
  const fromProblem = extractProblemMessage(error.problem)
  if (fromProblem) return fromProblem
  return tryExtractMessage(error.body)
}

/* ------------------------------- Data helpers ------------------------------ */

const UUID_RE =
  /^([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[1-8][0-9a-fA-F]{3}-[89abAB][0-9a-fA-F]{3}-[0-9a-fA-F]{12}|00000000-0000-0000-0000-000000000000|ffffffff-ffff-ffff-ffff-ffffffffffff)$/

const toUuidOrNull = (v: unknown): string | null =>
  typeof v === 'string' && UUID_RE.test(v) ? v : null

const toDateOrNull = (v: unknown): Date | null =>
  typeof v === 'string' && v.length > 0 ? new Date(v) : null

/* --------------------------- Inventory summary ---------------------------- */

export const fetchInventorySummary = async (): Promise<InventorySummary> => {
  const data = (await http(`${API_BASE}/inventories/summary`)) as Partial<InventorySummary> | null | undefined

  const sanitizeOpenRun = (run: Partial<OpenRunSummary> | null | undefined): OpenRunSummary => ({
    runId: typeof run?.runId === 'string' ? run.runId : '',
    locationId: typeof run?.locationId === 'string' ? run.locationId : '',
    locationCode: typeof run?.locationCode === 'string' ? run.locationCode : '',
    locationLabel: typeof run?.locationLabel === 'string' ? run.locationLabel : '',
    countType: (run?.countType as CountType) ?? CountType.Count1,
    ownerDisplayName: run?.ownerDisplayName ?? null,
    ownerUserId: typeof run?.ownerUserId === 'string' ? run.ownerUserId : null,
    startedAtUtc: typeof run?.startedAtUtc === 'string' ? run.startedAtUtc : '',
  })

  const sanitizeCompletedRun = (
    run: Partial<CompletedRunSummary> | null | undefined,
  ): CompletedRunSummary => ({
    runId: typeof run?.runId === 'string' ? run.runId : '',
    locationId: typeof run?.locationId === 'string' ? run.locationId : '',
    locationCode: typeof run?.locationCode === 'string' ? run.locationCode : '',
    locationLabel: typeof run?.locationLabel === 'string' ? run.locationLabel : '',
    countType: (run?.countType as CountType) ?? CountType.Count1,
    ownerDisplayName: run?.ownerDisplayName ?? null,
    ownerUserId: typeof run?.ownerUserId === 'string' ? run.ownerUserId : null,
    startedAtUtc: typeof run?.startedAtUtc === 'string' ? run.startedAtUtc : '',
    completedAtUtc: typeof run?.completedAtUtc === 'string' ? run.completedAtUtc : '',
  })

  const openRunDetails = (Array.isArray(data?.openRunDetails) ? data.openRunDetails : []).map(sanitizeOpenRun)
  const completedRunDetails = (Array.isArray(data?.completedRunDetails) ? data.completedRunDetails : []).map(
    sanitizeCompletedRun,
  )
  const conflictZones = Array.isArray(data?.conflictZones) ? data!.conflictZones : []

  return {
    activeSessions: data?.activeSessions ?? 0,
    openRuns: data?.openRuns ?? 0,
    completedRuns: data?.completedRuns ?? completedRunDetails.length,
    conflicts: data?.conflicts ?? 0,
    lastActivityUtc: data?.lastActivityUtc ?? null,
    openRunDetails,
    completedRunDetails,
    conflictZones,
  }
}

/* --------------------------- /api/locations schema ------------------------ */
/* Un SEUL schéma, réutilisé partout. On conserve countType/status. */

const LocationApiItemSchema = z
  .object({
    id: z.string().uuid(),
    label: z.string(),
    isBusy: z.boolean().optional().default(false),
    busyBy: z.string().nullable().optional(),
    activeRunId: z.string().uuid().nullable().optional(),
    activeCountType: z.number().int().nullable().optional(),
    activeStartedAtUtc: z.string().nullable().optional(),
    countStatuses: z
      .array(
        z
          .object({
            runId: z.string().uuid().nullable().optional(),
            ownerUserId: z.string().nullable().optional(),
            startedAtUtc: z.string().nullable().optional(),
            completedAtUtc: z.string().nullable().optional(),
            countType: z.number().int().nullable().optional(),
            status: z.enum(['not_started', 'in_progress', 'completed']).optional(),
          })
          .passthrough(),
      )
      .optional()
      .default([]),
  })
  .passthrough()

/* ------------------- Home: summaries (shape front propre) ----------------- */

export const fetchLocationSummaries = async (
  shopId: string,
  signal?: AbortSignal,
): Promise<LocationSummaryList> => {
  if (!shopId) throw new Error('Aucune boutique sélectionnée.')

  const searchParams = new URLSearchParams({ shopId })
  const raw = await http(`${API_BASE}/locations?${searchParams.toString()}`, { signal })

  const normalized = Array.isArray(raw) ? raw : []
  const apiItems = z.array(LocationApiItemSchema).parse(normalized)

  const adapted = apiItems.map((it) => ({
    locationId: it.id,
    locationName: it.label,
    busyBy: it.busyBy ?? null,
    activeRunId: it.activeRunId ?? null,
    activeCountType: it.activeCountType ?? null,
    activeStartedAtUtc: toDateOrNull(it.activeStartedAtUtc),
    // ici on expose uniquement ce que le schéma front attend
    countStatuses: (it.countStatuses ?? []).map((s) => ({
      runId: s.runId ?? null,
      ownerUserId: toUuidOrNull(s.ownerUserId),
      startedAtUtc: toDateOrNull(s.startedAtUtc),
      completedAtUtc: toDateOrNull(s.completedAtUtc),
    })),
  }))

  return LocationSummaryListSchema.parse(adapted)
}

/* -------------------------- Conflits & détails run ------------------------ */

export const getConflictZonesSummary = async (): Promise<ConflictZoneSummary[]> => {
  const summary = await fetchInventorySummary()
  return summary.conflictZones
}

export const getConflictZoneDetail = async (
  locationId: string,
  signal?: AbortSignal,
): Promise<ConflictZoneDetail> => {
  const data = await http(`${API_BASE}/conflicts/${encodeURIComponent(locationId)}`, { signal })
  return data as ConflictZoneDetail
}

export const getCompletedRunDetail = async (runId: string): Promise<CompletedRunDetail> => {
  const data = (await http(`${API_BASE}/inventories/runs/${encodeURIComponent(runId)}`)) as
    | Partial<CompletedRunDetail>
    | null
    | undefined

  const items = Array.isArray(data?.items) ? data!.items : []

  return {
    runId: data?.runId ?? runId,
    locationId: data?.locationId ?? '',
    locationCode: data?.locationCode ?? '',
    locationLabel: data?.locationLabel ?? '',
    countType: (data?.countType as CountType) ?? CountType.Count1,
    ownerDisplayName: data?.ownerDisplayName ?? null,
    ownerUserId: data?.ownerUserId ?? null,
    startedAtUtc: data?.startedAtUtc ?? '',
    completedAtUtc: data?.completedAtUtc ?? '',
    items: items.map((item) => ({
      productId: item?.productId ?? '',
      sku: item?.sku ?? '',
      name: item?.name ?? '',
      ean: typeof item?.ean === 'string' && item.ean.trim().length > 0 ? item.ean : null,
      quantity: typeof item?.quantity === 'number' ? item.quantity : Number(item?.quantity ?? 0),
    })),
  }
}

/* ------------------------- /api/locations: select list -------------------- */
/* Parse brut -> normalise UUID -> validation stricte LocationsSchema */

export const fetchLocations = async (shopId: string): Promise<Location[]> => {
  if (!shopId) throw new Error('Aucune boutique sélectionnée.')

  const url = `${API_BASE}/locations?shopId=${encodeURIComponent(shopId)}`
  const response = await http(url)

  const arr = z.array(LocationApiItemSchema).parse(response)

  const sanitized = arr.map((loc) => ({
    ...loc,
    countStatuses: loc.countStatuses.map((s) => ({
      ...s, // on préserve countType/status/runId/started/completed
      ownerUserId: toUuidOrNull(s.ownerUserId),
    })),
  }))

  return LocationsSchema.parse(sanitized)
}

/* ----------------------------- Produits par EAN --------------------------- */

export const fetchProductByEan = async (ean: string): Promise<Product> => {
  const trimmed = (ean ?? '').trim()
  if (trimmed.length === 0 || !/^\d+$/.test(trimmed)) {
    throw new Error("EAN invalide (valeur vide ou non numérique)")
  }
  const data = await http(`${API_BASE}/products/${encodeURIComponent(trimmed)}`)
  return data as Product
}

/* --------------------------- Runs start/complete -------------------------- */

export interface CompleteInventoryRunItem {
  ean: string
  quantity: number
  isManual: boolean
}

export interface CompleteInventoryRunPayload {
  runId?: string | null
  ownerUserId: string
  countType: number
  items: CompleteInventoryRunItem[]
}

export interface StartInventoryRunPayload {
  shopId: string
  ownerUserId: string
  countType: number
}

export interface StartInventoryRunResponse {
  runId: string
  inventorySessionId: string
  locationId: string
  countType: number
  ownerDisplayName: string | null
  ownerUserId: string | null
  startedAtUtc: string
}

export interface CompleteInventoryRunResponse {
  runId: string
  inventorySessionId: string
  locationId: string
  countType: number
  completedAtUtc: string
  itemsCount: number
  totalQuantity: number
}

export const startInventoryRun = async (
  locationId: string,
  payload: StartInventoryRunPayload,
): Promise<StartInventoryRunResponse> => {
  const url = `${API_BASE}/inventories/${encodeURIComponent(locationId)}/start`
  return (await http(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })) as StartInventoryRunResponse
}

export const completeInventoryRun = async (
  locationId: string,
  payload: CompleteInventoryRunPayload,
  signal?: AbortSignal,
): Promise<CompleteInventoryRunResponse> => {
  const url = `${API_BASE}/inventories/${encodeURIComponent(locationId)}/complete`
  try {
    return (await http(url, {
      method: 'POST',
      body: payload,
      signal,
    })) as CompleteInventoryRunResponse
  } catch (error) {
    if (isHttpError(error)) {
      const messageFromBody = getHttpErrorMessage(error)
      const problemPayload = error.problem ?? (error.body ? { body: error.body } : undefined)

      if (error.status === 415) {
        throw Object.assign(
          new Error('Unsupported Media Type: requête JSON attendue (Content-Type: application/json).'),
          { status: error.status, problem: problemPayload },
        )
      }

      let message: string
      if (error.status === 400) message = messageFromBody ?? 'Requête invalide.'
      else if (error.status === 404) message = 'Zone introuvable pour ce comptage.'
      else message = messageFromBody ?? `Impossible de terminer le comptage (HTTP ${error.status}).`

      throw Object.assign(new Error(message), {
        status: error.status,
        problem: problemPayload,
      })
    }

    throw error
  }
}

export const restartInventoryRun = async (locationId: string, countType: CountType): Promise<void> => {
  const searchParams = new URLSearchParams({ countType: String(countType) })
  await http(`${API_BASE}/inventories/${locationId}/restart?${searchParams.toString()}`, {
    method: 'POST',
  })
}

export const releaseInventoryRun = async (
  locationId: string,
  runId: string,
  ownerUserId: string,
): Promise<void> => {
  const url = `${API_BASE}/inventories/${encodeURIComponent(locationId)}/release`
  try {
    await http(url, {
      method: 'POST',
      body: { runId, ownerUserId },
    })
  } catch (error) {
    if (isHttpError(error)) {
      const messageFromBody = getHttpErrorMessage(error)
      let message = `Impossible de libérer le comptage (HTTP ${error.status}).`
      if (error.status === 404) message = messageFromBody ?? 'Comptage introuvable.'
      else if (error.status === 409) message = messageFromBody ?? 'Comptage déjà détenu par un autre utilisateur.'
      else if (error.status === 400) message = messageFromBody ?? 'Requête invalide.'
      else if (messageFromBody) message = messageFromBody

      throw Object.assign(new Error(message), {
        status: error.status,
        url: url,
        problem: (error as HttpError).problem,
      })
    }

    throw error
  }
}
