import type { ReactNode } from 'react'
import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import { login as loginRequest } from '../api/authApi'
import { onUnauthorized, setAuthToken } from '../api/client'
import type { AuthUser } from '../types/auth'

interface AuthContextValue {
  user: AuthUser | null
  token: string | null
  isAuthenticated: boolean
  loading: boolean
  initialising: boolean
  login: (username: string, password: string) => Promise<void>
  logout: () => void
}

const STORAGE_KEY = 'inventory-web-auth'

const AuthContext = createContext<AuthContextValue | undefined>(undefined)

export const AuthProvider = ({ children }: { children: ReactNode }) => {
  const [user, setUser] = useState<AuthUser | null>(null)
  const [token, setToken] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [initialising, setInitialising] = useState(true)

  useEffect(() => {
    const stored = localStorage.getItem(STORAGE_KEY)
    if (stored) {
      try {
        const parsed = JSON.parse(stored) as { token: string; user: AuthUser }
        setUser(parsed.user)
        setToken(parsed.token)
        setAuthToken(parsed.token)
      } catch (error) {
        console.error('Impossible de restaurer la session', error)
        localStorage.removeItem(STORAGE_KEY)
      }
    }
    setInitialising(false)
  }, [])

  useEffect(() => {
    onUnauthorized(() => {
      localStorage.removeItem(STORAGE_KEY)
      setUser(null)
      setToken(null)
      setAuthToken(null)
    })
  }, [])

  const login = useCallback(async (username: string, password: string) => {
    setLoading(true)
    try {
      const { token: receivedToken, user: receivedUser } = await loginRequest({ username, password })
      setUser(receivedUser)
      setToken(receivedToken)
      setAuthToken(receivedToken)
      localStorage.setItem(STORAGE_KEY, JSON.stringify({ token: receivedToken, user: receivedUser }))
    } finally {
      setLoading(false)
    }
  }, [])

  const logout = useCallback(() => {
    localStorage.removeItem(STORAGE_KEY)
    setUser(null)
    setToken(null)
    setAuthToken(null)
  }, [])

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      token,
      isAuthenticated: Boolean(user && token),
      loading,
      initialising,
      login,
      logout,
    }),
    [initialising, loading, login, logout, token, user],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export const useAuth = () => {
  const context = useContext(AuthContext)
  if (!context) {
    throw new Error('useAuth doit être utilisé dans AuthProvider')
  }
  return context
}
