export interface AuthUser {
  id: string
  name: string
  roles: string[]
}

export interface AuthResponse {
  token: string
  user: AuthUser
}
