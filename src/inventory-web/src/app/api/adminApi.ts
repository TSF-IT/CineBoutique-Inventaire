import type { Location } from '../types/inventory'
import http from '@/lib/api/http'
import { API_BASE } from '@/lib/api/config'

interface LocationPayload {
  label: string
  code?: string
  description?: string
}

export const createLocation = async (payload: LocationPayload): Promise<Location> => {
  const data = await http(`${API_BASE}/locations`, {
    method: 'POST',
    body: JSON.stringify(payload),
  })
  return data as Location
}

export const updateLocation = async (
  id: string,
  payload: Partial<LocationPayload>,
): Promise<Location> => {
  const data = await http(`${API_BASE}/locations/${id}`, {
    method: 'PUT',
    body: JSON.stringify(payload),
  })
  return data as Location
}

export const deleteLocation = async (id: string): Promise<void> => {
  await http(`${API_BASE}/locations/${id}`, {
    method: 'DELETE',
  })
}
