import type { Location } from '../types/inventory'
import { http } from '../../lib/api/http'

interface LocationPayload {
  label: string
  code?: string
  description?: string
}

export const createLocation = async (payload: LocationPayload): Promise<Location> =>
  http<Location>('/locations', {
    method: 'POST',
    body: JSON.stringify(payload),
  })

export const updateLocation = async (
  id: string,
  payload: Partial<LocationPayload>,
): Promise<Location> =>
  http<Location>(`/locations/${id}`, {
    method: 'PUT',
    body: JSON.stringify(payload),
  })

export const deleteLocation = async (id: string): Promise<void> =>
  http<void>(`/locations/${id}`, {
    method: 'DELETE',
  })
