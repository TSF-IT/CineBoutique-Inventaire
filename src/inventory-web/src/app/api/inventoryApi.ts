import { z } from 'zod'
import { CountType, LocationsSchema } from '../types/inventory'
import type { InventorySummary, Location, Product } from '../types/inventory'
import http, { HttpError } from '@/lib/api/http'
import { API_BASE } from '@/lib/api/config'
import { areDevFixturesEnabled, cloneDevLocations } from './dev/fixtures'

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
  const conflictDetails = Array.isArray(data?.conflictDetails) ? data!.conflictDetails : []

  return {
    activeSessions: data?.activeSessions ?? 0,
    openRuns: data?.openRuns ?? 0,
    conflicts: data?.conflicts ?? 0,
    lastActivityUtc: data?.lastActivityUtc ?? null,
    openRunDetails,
    conflictDetails,
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
