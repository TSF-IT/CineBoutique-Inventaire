import { CountType, LocationsSchema } from '../types/inventory'
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
  if (!problem) {
    return null
  }

  if (typeof problem === 'string') {
    return tryExtractMessage(problem)
  }

  if (Array.isArray(problem)) {
    for (const item of problem) {
      const message = extractProblemMessage(item)
      if (message) {
        return message
      }
    }
    return null
  }

  if (typeof problem !== 'object') {
    return null
  }

  const record = problem as Record<string, unknown>
  const candidateKeys = ['message', 'detail', 'title', 'error', 'error_description'] as const
  for (const key of candidateKeys) {
    const message = tryExtractMessage(record[key])
    if (message) {
      return message
    }
  }

  const errors = record.errors
  if (Array.isArray(errors)) {
    for (const item of errors) {
      const message = extractProblemMessage(item)
      if (message) {
        return message
      }
    }
  }

  return null
}

const getHttpErrorMessage = (error: HttpError): string | null => {
  const fromProblem = extractProblemMessage(error.problem)
  if (fromProblem) {
    return fromProblem
  }
  return tryExtractMessage(error.body)
}

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
  const completedRunDetails = (Array.isArray(data?.completedRunDetails)
    ? data.completedRunDetails
    : []
  ).map(sanitizeCompletedRun)
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

export const getConflictZonesSummary = async (): Promise<ConflictZoneSummary[]> => {
  const summary = await fetchInventorySummary()
  return summary.conflictZones
}

export const getConflictZoneDetail = async (
  locationId: string,
  signal?: AbortSignal,
): Promise<ConflictZoneDetail> => {
  const data = await http(`${API_BASE}/conflicts/${encodeURIComponent(locationId)}`, {
    signal,
  })
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

export const fetchLocations = async (shopId: string): Promise<Location[]> => {
  if (!shopId) {
    throw new Error('Aucune boutique sélectionnée.')
  }

  const url = `${API_BASE}/locations?shopId=${encodeURIComponent(shopId)}`
  const response = await http(url)
  return LocationsSchema.parse(response)
}

export const fetchProductByEan = async (ean: string): Promise<Product> => {
  const data = await http(`${API_BASE}/products/${encodeURIComponent(ean)}`)
  return data as Product
}

export interface CompleteInventoryRunItem {
  ean: string
  quantity: number
  isManual: boolean
}

export interface CompleteInventoryRunPayload {
  runId?: string | null
  ownerUserId: string
  countType: 1 | 2 | 3
  items: CompleteInventoryRunItem[]
}

export interface StartInventoryRunPayload {
  shopId: string
  ownerUserId: string
  countType: 1 | 2 | 3
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
      if (error.status === 400) {
        message = messageFromBody ?? 'Requête invalide.'
      } else if (error.status === 404) {
        message = 'Zone introuvable pour ce comptage.'
      } else {
        message = messageFromBody ?? `Impossible de terminer le comptage (HTTP ${error.status}).`
      }

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
      else if (error.status === 409)
        message = messageFromBody ?? 'Comptage déjà détenu par un autre utilisateur.'
      else if (error.status === 400) message = messageFromBody ?? 'Requête invalide.'
      else if (messageFromBody) message = messageFromBody
      throw Object.assign(new Error(message), {
        status: error.status,
        url: error.url,
        problem: error.problem,
      })
    }

    throw error
  }
}
