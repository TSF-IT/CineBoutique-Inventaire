import axios from 'axios'

const API_BASE_URL = import.meta.env.VITE_API_URL ?? '/api'

let authToken: string | null = null
let unauthorizedCallback: (() => void) | undefined

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
    return Promise.reject(error)
  },
)

export const setAuthToken = (token: string | null) => {
  authToken = token
}

export const onUnauthorized = (callback: () => void) => {
  unauthorizedCallback = callback
}

export { API_BASE_URL }
