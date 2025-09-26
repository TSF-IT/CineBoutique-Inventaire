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

const normaliseLocationsPayload = (payload: unknown): unknown => {
  if (Array.isArray(payload)) {
    return payload
  }
  if (payload && typeof payload === 'object') {
    const candidateKeys = ['items', 'data', 'results', 'locations'] as const
    for (const key of candidateKeys) {
      const value = (payload as Record<string, unknown>)[key]
      if (Array.isArray(value)) {
        return value
      }
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
      if (import.meta.env.DEV) {
        console.error('Réponse /locations invalide', parsed.error.flatten())
      }
      throw new Error('Les données de zones sont invalides.')
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
