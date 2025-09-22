import type { AuthResponse } from '../types/auth'
import { http } from '../../lib/api/http'

interface Credentials {
  username: string
  password: string
}

export const login = async (credentials: Credentials): Promise<AuthResponse> =>
  http<AuthResponse>('/auth/login', {
    method: 'POST',
    body: JSON.stringify(credentials),
  })
