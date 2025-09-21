import type { AuthResponse } from '../types/auth'
import { apiClient } from './client'

interface Credentials {
  username: string
  password: string
}

export const login = async (credentials: Credentials): Promise<AuthResponse> => {
  const { data } = await apiClient.post<AuthResponse>('/auth/login', credentials)
  return data
}
