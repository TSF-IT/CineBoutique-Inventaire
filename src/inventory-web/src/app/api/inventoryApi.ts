import { z } from 'zod'
import { CountType, LocationsSchema } from '../types/inventory'
import type { CompletedRunDetail } from '../types/inventory'
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

const toHttpError = (message: string, url: string, problem?: unknown): HttpError =>
  Object.assign(new Error(message), {
    status: 0,
    url,
    problem,
  })

const shouldUseDevFixtures = (error: unknown): boolean => {
  if (!areDevFixturesEnabled()) {
    return false
  }

  if (!error) {
    return true
  }

  if (!isHttpError(error)) {
    return true
  }

  return error.status === 0 || error.status >= 500
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

  const openRunDetails = Array.isArray(data?.openRunDetails) ? data!.openRunDetails : []
  const completedRunDetails = Array.isArray(data?.completedRunDetails) ? data!.completedRunDetails : []
  const conflictZones = Array.isArray(data?.conflictZones) ? data!.conflictZones : []

  return {
    activeSessions: data?.activeSessions ?? 0,
    openRuns: data?.openRuns ?? 0,
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
    operatorDisplayName: data?.operatorDisplayName ?? null,
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

export const fetchLocations = async (options?: { countType?: CountType }): Promise<Location[]> => {
  let endpoint = '/locations'
  try {
    const searchParams = new URLSearchParams()
    if (options?.countType !== undefined) {
      searchParams.set('countType', String(options.countType))
    }
    const query = searchParams.toString()
    endpoint = `/locations${query ? `?${query}` : ''}`
    const raw = await http(`${API_BASE}${endpoint}`)
    const payload = typeof raw === 'string' ? JSON.parse(raw) : raw
    const normalised = normaliseLocationsPayload(payload)
    try {
      return LocationsSchema.parse(normalised)
    } catch (error) {
      if (error instanceof z.ZodError) {
        console.warn('Validation /locations failed', error.flatten())
      }
      throw error
    }
  } catch (error) {
    if (shouldUseDevFixtures(error)) {
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
  countType: 1 | 2 | 3
  operator: string
  items: CompleteInventoryRunItem[]
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

export const completeInventoryRun = async (
  locationId: string,
  payload: CompleteInventoryRunPayload,
  signal?: AbortSignal,
): Promise<CompleteInventoryRunResponse> => {
  const url = `${API_BASE}/inventories/${encodeURIComponent(locationId)}/complete`

  const res = await fetch(url, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(payload),
    signal,
  })

  if (!res.ok) {
    if (res.status === 404 && areDevFixturesEnabled()) {
      const devLocation = findDevLocationById(locationId)
      if (devLocation) {
        const fallbackError = Object.assign(new Error('HTTP 404'), { status: res.status, url })
        logDevFallback(fallbackError, url)
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

    let parsedBody: unknown = null
    let bodyText = ''
    let messageFromBody: string | null = null

    try {
      const contentType = res.headers.get('content-type') ?? ''
      if (contentType.toLowerCase().includes('application/json')) {
        parsedBody = await res.json()
        if (parsedBody && typeof parsedBody === 'object') {
          const candidates = [
            (parsedBody as { message?: unknown }).message,
            (parsedBody as { detail?: unknown }).detail,
            (parsedBody as { title?: unknown }).title,
          ]
          const chosen = candidates.find((value) => typeof value === 'string' && value.trim().length > 0) as
            | string
            | undefined
          if (chosen) {
            messageFromBody = chosen.trim()
          }
        }
      } else {
        bodyText = await res.text()
        if (bodyText.trim()) {
          messageFromBody = bodyText.trim()
        }
      }
    } catch {
      // Lecture best effort, status traité ci-dessous
    }

    const problemPayload = parsedBody ?? (bodyText ? { body: bodyText } : undefined)

    if (res.status === 415) {
      throw Object.assign(
        new Error('Unsupported Media Type: requête JSON attendue (Content-Type: application/json).'),
        { status: res.status, problem: problemPayload },
      )
    }

    let message: string
    if (res.status === 400) {
      message = messageFromBody ?? 'Requête invalide.'
    } else if (res.status === 404) {
      message = 'Zone introuvable pour ce comptage.'
    } else {
      message = messageFromBody ?? `Impossible de terminer le comptage (HTTP ${res.status}).`
    }

    throw Object.assign(new Error(message), {
      status: res.status,
      problem: problemPayload,
    })
  }

  return (await res.json()) as CompleteInventoryRunResponse
}

export const restartInventoryRun = async (locationId: string, countType: CountType): Promise<void> => {
  const searchParams = new URLSearchParams({ countType: String(countType) })
  await http(`${API_BASE}/inventories/${locationId}/restart?${searchParams.toString()}`, {
    method: 'POST',
  })
}
