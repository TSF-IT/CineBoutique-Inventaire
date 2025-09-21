import type { Location } from '../types/inventory'
import { apiClient } from './client'

interface CreateLocationPayload {
  name: string
  description?: string
}

export const createLocation = async (payload: CreateLocationPayload): Promise<Location> => {
  const { data } = await apiClient.post<Location>('/locations', payload)
  return data
}

export const updateLocation = async (
  id: string,
  payload: Partial<CreateLocationPayload>,
): Promise<Location> => {
  const { data } = await apiClient.put<Location>(`/locations/${id}`, payload)
  return data
}

export const deleteLocation = async (id: string): Promise<void> => {
  await apiClient.delete(`/locations/${id}`)
}
