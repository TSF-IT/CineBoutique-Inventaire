import type { AuthResponse } from '../types/auth'
import http from '@/lib/api/http'
import { API_BASE } from '@/lib/api/config'

interface Credentials {
  username: string
  password: string
}

export const login = async (credentials: Credentials): Promise<AuthResponse> => {
  const data = await http(`${API_BASE}/auth/login`, {
    method: 'POST',
    body: JSON.stringify(credentials),
  })
  return data as AuthResponse
}
