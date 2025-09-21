import axios from 'axios'

const DEFAULT_API_BASE_URL = 'http://localhost:8080/api'
const API_BASE_URL =
  import.meta.env.VITE_API_BASE_URL ?? import.meta.env.VITE_API_URL ?? DEFAULT_API_BASE_URL

let authToken: string | null = null
let unauthorizedCallback: (() => void) | undefined

export class ApiError extends Error {
  status?: number
  data?: unknown

  constructor(message: string, options?: { status?: number; data?: unknown; cause?: unknown }) {
    super(message)
    this.name = 'ApiError'
    this.status = options?.status
    this.data = options?.data
    if (options?.cause) {
      ;(this as { cause?: unknown }).cause = options.cause
    }
  }
}

export const apiClient = axios.create({
  baseURL: API_BASE_URL,
  timeout: 10000,
})

apiClient.interceptors.request.use((config) => {
  if (authToken) {
    config.headers = config.headers ?? {}
    config.headers.Authorization = `Bearer ${authToken}`
  }
  return config
})

apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401 && unauthorizedCallback) {
      unauthorizedCallback()
    }
    const status = error.response?.status
    const data = error.response?.data
    const message =
      (typeof data?.message === 'string' && data.message) ||
      error.message ||
      'Une erreur réseau est survenue'

    if (import.meta.env.DEV) {
      console.error('Appel API en erreur', {
        message,
        status,
        url: error.config?.url,
        data,
      })
    }

    return Promise.reject(new ApiError(message, { status, data, cause: error }))
  },
)

export const setAuthToken = (token: string | null) => {
  authToken = token
}

export const onUnauthorized = (callback: () => void) => {
  unauthorizedCallback = callback
}

export { API_BASE_URL }
