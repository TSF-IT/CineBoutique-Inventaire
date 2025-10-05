import { z } from 'zod'
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
import { areDevFixturesEnabled, cloneDevLocations, findDevLocationById } from './dev/fixtures'

const isHttpError = (value: unknown): value is HttpError =>
  typeof value === 'object' &&
  value !== null &&
  typeof (value as { status?: unknown }).status === 'number' &&
  typeof (value as { url?: unknown }).url === 'string'

const toHttpError = (message: string, url: string, problem?: unknown, status = 0): HttpError =>
  Object.assign(new Error(message), {
    status,
    url,
    problem,
  })

const shouldUseDevFixtures = (error: unknown, { httpCallSucceeded }: { httpCallSucceeded: boolean }): boolean => {
  if (!import.meta.env.DEV || !areDevFixturesEnabled()) {
    return false
  }

  if (httpCallSucceeded) {
    return false
  }

  if (!error) {
    return true
  }

  if (!isHttpError(error)) {
    return true
  }

  if (error.status === 422) {
    return false
  }

  if (error.status === 0 || error.status >= 500) {
    return true
  }

  return error.status >= 400
}

const logDevFallback = (error: unknown, endpoint: string) => {
  if (!import.meta.env.DEV) {
    return
  }

  const details = isHttpError(error)
    ? `statut HTTP ${error.status}${error.url ? ` (${error.url})` : ''}`
    : error instanceof Error
      ? error.message
      : String(error)
  console.warn(
    `[DEV] Fallback fixtures pour ${endpoint} : ${details}. Activez VITE_DISABLE_DEV_FIXTURES=true pour désactiver cette aide.`,
    error,
  )
}

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

const generateUuid = (): string => {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }

  const template = 'xxxxxxxx-xxxx-4xxx-8xxx-xxxxxxxxxxxx'
  return template.replace(/[xy]/g, (char) => {
    const random = Math.floor(Math.random() * 16)
    if (char === 'x') {
      return random.toString(16)
    }
    return ((random & 0x3) | 0x8).toString(16)
  })
}

const UPPERCASE_LOCATION_KEY_MAP: Record<string, keyof Location> = {
  Id: 'id',
  Code: 'code',
  Label: 'label',
  IsBusy: 'isBusy',
  BusyBy: 'busyBy',
  ActiveRunId: 'activeRunId',
  ActiveCountType: 'activeCountType',
  ActiveStartedAtUtc: 'activeStartedAtUtc',
}

const mapUppercaseLocationKeys = (value: unknown): unknown => {
  if (!Array.isArray(value) || value.length === 0) {
    return value
  }
  const first = value[0]
  if (!first || typeof first !== 'object') {
    return value
  }
  const firstKeys = Object.keys(first as Record<string, unknown>)
  const hasUppercaseKeys = firstKeys.some((key) => key in UPPERCASE_LOCATION_KEY_MAP)
  if (!hasUppercaseKeys) {
    return value
  }
  return value.map((item) => {
    if (!item || typeof item !== 'object') {
      return item
    }
    const source = item as Record<string, unknown>
    const mappedEntries = Object.entries(UPPERCASE_LOCATION_KEY_MAP).reduce<Record<string, unknown>>(
      (acc, [sourceKey, targetKey]) => {
        if (sourceKey in source) {
          acc[targetKey] = source[sourceKey]
        }
        return acc
      },
      {},
    )
    return { ...source, ...mappedEntries }
  })
}

const normaliseLocationsPayload = (payload: unknown): unknown => {
  if (Array.isArray(payload)) {
    return mapUppercaseLocationKeys(payload)
  }
  if (payload && typeof payload === 'object') {
    const candidateKeys = ['items', 'data', 'results', 'locations'] as const
    for (const key of candidateKeys) {
      const value = (payload as Record<string, unknown>)[key]
      if (Array.isArray(value)) {
        return mapUppercaseLocationKeys(value)
      }
    }

    const arrayValues = Object.values(payload as Record<string, unknown>).filter(Array.isArray)
    if (arrayValues.length === 1) {
      return mapUppercaseLocationKeys(arrayValues[0])
    }
  }
  return payload
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

export const fetchLocations = async ({ shopId, countType }: { shopId: string; countType?: CountType }): Promise<Location[]> => {
  const normalisedShopId = shopId?.trim()
  if (!normalisedShopId) {
    throw new Error('shopId requis pour récupérer les zones.')
  }

  let endpoint = '/locations'
  let httpCallSucceeded = false
  try {
    const searchParams = new URLSearchParams()
    searchParams.set('shopId', normalisedShopId)
    if (countType !== undefined) {
      searchParams.set('countType', String(countType))
    }
    const query = searchParams.toString()
    endpoint = `/locations${query ? `?${query}` : ''}`
    const raw = await http(`${API_BASE}${endpoint}`)
    httpCallSucceeded = true
    const payload = typeof raw === 'string' ? JSON.parse(raw) : raw
    const normalised = normaliseLocationsPayload(payload)
    try {
      return LocationsSchema.parse(normalised)
    } catch (error) {
      if (error instanceof z.ZodError) {
        if (import.meta.env.DEV) {
          console.warn('Validation /locations failed', error.flatten())
        }
        throw toHttpError(
          'Réponse /locations invalide.',
          `${API_BASE}${endpoint}`,
          error.flatten(),
          422,
        )
      }
      throw error
    }
  } catch (error) {
    if (shouldUseDevFixtures(error, { httpCallSucceeded })) {
      logDevFallback(error, `${API_BASE}${endpoint}`)
      return cloneDevLocations()
    }

    if (isHttpError(error)) {
      throw error
    }
    if (import.meta.env.DEV) {
      console.error('Échec de récupération des zones', error)
    }
    const problem = error instanceof z.ZodError ? error.flatten() : error
    throw toHttpError('Impossible de récupérer les zones.', `${API_BASE}${endpoint}`, problem)
  }
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
      if (error.status === 404 && areDevFixturesEnabled()) {
        const devLocation = findDevLocationById(locationId)
        if (devLocation) {
          logDevFallback(error, url)
          const completionDate = new Date()
          const runId =
            typeof payload.runId === 'string' && payload.runId.trim().length > 0
              ? payload.runId.trim()
              : generateUuid()
          const totalQuantity = payload.items.reduce((sum, item) => sum + item.quantity, 0)
          return {
            runId,
            inventorySessionId: generateUuid(),
            locationId: devLocation.id,
            countType: payload.countType,
            completedAtUtc: completionDate.toISOString(),
            itemsCount: payload.items.length,
            totalQuantity,
          }
        }
      }

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
