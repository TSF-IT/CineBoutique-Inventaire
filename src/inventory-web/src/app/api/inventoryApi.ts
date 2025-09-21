import type {
  CountType,
  InventoryCheckResponse,
  InventorySummary,
  Location,
  ManualProductInput,
  Product,
} from '../types/inventory'
import { apiClient } from './client'

export const fetchInventorySummary = async (): Promise<InventorySummary> => {
  const { data } = await apiClient.get<InventorySummary>('/inventories/summary')
  return data
}

export const fetchLocations = async (): Promise<Location[]> => {
  const { data } = await apiClient.get<Location[]>('/locations')
  return data
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
