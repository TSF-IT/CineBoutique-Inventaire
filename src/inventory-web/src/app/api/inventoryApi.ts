import { LocationsSchema } from '../types/inventory'
import type { CountType, InventorySummary, Location, ManualProductInput, Product } from '../types/inventory'
import { http, HttpError } from '../../lib/api/http'
import { API_BASE } from '../../lib/api/config'

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

export const fetchInventorySummary = (): Promise<InventorySummary> =>
  http<InventorySummary>('/inventories/summary')

export const fetchLocations = async (options?: { countType?: CountType }): Promise<Location[]> => {
  let endpoint = '/locations'
  try {
    const searchParams = new URLSearchParams()
    if (options?.countType !== undefined) {
      searchParams.set('countType', String(options.countType))
    }
    const query = searchParams.toString()
    endpoint = `/locations${query ? `?${query}` : ''}`
    const data = await http<unknown>(endpoint)
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
    if (error instanceof HttpError) {
      throw error
    }
    if (import.meta.env.DEV) {
      console.error('Échec de récupération des zones', error)
    }
    throw new HttpError('Impossible de récupérer les zones.', undefined, undefined, `${API_BASE}${endpoint}`)
  }
}

export const fetchProductByEan = async (ean: string): Promise<Product> => {
  return http<Product>(`/products/${encodeURIComponent(ean)}`)
}

export const createManualProduct = async (payload: ManualProductInput): Promise<Product> => {
  return http<Product>('/products', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export const restartInventoryRun = async (locationId: string, countType: CountType): Promise<void> => {
  const searchParams = new URLSearchParams({ countType: String(countType) })
  await http<void>(`/inventories/${locationId}/restart?${searchParams.toString()}`, {
    method: 'POST',
  })
}
