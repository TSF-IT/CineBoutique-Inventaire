import { LocationsSchema } from '../types/inventory'
import type {
  CountType,
  InventoryCheckResponse,
  InventorySummary,
  Location,
  ManualProductInput,
  Product,
} from '../types/inventory'
import { ApiError, apiClient } from './client'

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
  const { data } = await apiClient.get<InventorySummary>('/inventories/summary')
  return data
}

export const fetchLocations = async (): Promise<Location[]> => {
  try {
    const { data } = await apiClient.get('/locations')
    const normalised = normaliseLocationsPayload(data)
    const parsed = LocationsSchema.safeParse(normalised)
    if (!parsed.success) {
      if (import.meta.env.DEV) {
        console.error('Réponse /locations invalide', parsed.error.flatten())
      }
      throw new ApiError('Les données de zones sont invalides.', { data, cause: parsed.error })
    }
    return parsed.data
  } catch (error) {
    if (error instanceof ApiError) {
      throw error
    }
    if (import.meta.env.DEV) {
      console.error('Échec de récupération des zones', error)
    }
    throw new ApiError('Impossible de récupérer les zones.', { cause: error })
  }
}

export const verifyInventoryInProgress = async (
  locationId: string,
  countType: CountType,
): Promise<InventoryCheckResponse> => {
  const { data } = await apiClient.get<InventoryCheckResponse>('/inventories/check', {
    params: { locationId, countType },
  })
  return data
}

export const fetchProductByEan = async (ean: string): Promise<Product> => {
  const { data } = await apiClient.get<Product>(`/products/${ean}`)
  return data
}

export const createManualProduct = async (payload: ManualProductInput): Promise<Product> => {
  const { data } = await apiClient.post<Product>('/products', payload)
  return data
}
