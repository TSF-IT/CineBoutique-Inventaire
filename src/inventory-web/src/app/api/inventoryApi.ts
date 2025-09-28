import { CountType, LocationsSchema } from '../types/inventory'
import type { InventorySummary, Location, ManualProductInput, Product } from '../types/inventory'
import http, { HttpError } from '@/lib/api/http'
import { API_BASE } from '@/lib/api/config'

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
  const data = await http(`${API_BASE}/inventories/summary`)
  return data as InventorySummary
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
    const data = await http(`${API_BASE}${endpoint}`)
    const normalised = normaliseLocationsPayload(data)
    const parsed = LocationsSchema.safeParse(normalised)
    if (!parsed.success) {
      const zodError = parsed.error
      const flattened = zodError.flatten()
      if (import.meta.env.DEV) {
        let sample: string | undefined
        try {
          sample = JSON.stringify(normalised)
        } catch {
          sample = undefined
        }
        console.warn('Réponse /locations invalide', flattened, {
          sample: sample?.slice(0, 512),
        })
      }
      throw toHttpError('Impossible de récupérer les zones.', `${API_BASE}${endpoint}`, flattened)
    }
    return parsed.data
  } catch (error) {
    if (isHttpError(error)) {
      throw error
    }
    if (import.meta.env.DEV) {
      console.error('Échec de récupération des zones', error)
    }
    throw toHttpError('Impossible de récupérer les zones.', `${API_BASE}${endpoint}`, error)
  }
}

export const fetchProductByEan = async (ean: string): Promise<Product> => {
  const data = await http(`${API_BASE}/products/${encodeURIComponent(ean)}`)
  return data as Product
}

export const createManualProduct = async (payload: ManualProductInput): Promise<Product> => {
  const data = await http(`${API_BASE}/products`, {
    method: 'POST',
    body: JSON.stringify(payload),
  })
  return data as Product
}

export const restartInventoryRun = async (locationId: string, countType: CountType): Promise<void> => {
  const searchParams = new URLSearchParams({ countType: String(countType) })
  await http(`${API_BASE}/inventories/${locationId}/restart?${searchParams.toString()}`, {
    method: 'POST',
  })
}
