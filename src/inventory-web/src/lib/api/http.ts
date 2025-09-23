import { API_BASE } from './config'

type HttpMethod = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'

let authToken: string | null = null
let unauthorizedCallback: (() => void) | undefined

export class HttpError extends Error {
  status?: number
  bodyText?: string
  url?: string

  constructor(message: string, status?: number, bodyText?: string, url?: string) {
    super(message)
    this.name = 'HttpError'
    this.status = status
    this.bodyText = bodyText
    this.url = url
  }
}

const MAX_DEBUG_BODY_LENGTH = 600

const truncate = (value: string | undefined): string | undefined => {
  if (!value) {
    return value
  }
  return value.length <= MAX_DEBUG_BODY_LENGTH ? value : `${value.slice(0, MAX_DEBUG_BODY_LENGTH)}…`
}

const safeText = async (response: Response): Promise<string | undefined> => {
  try {
    return await response.text()
  } catch {
    return undefined
  }
}

export const DEV_API_UNREACHABLE_HINT =
  "Impossible de joindre l’API : vérifie que le backend tourne (curl http://localhost:5000/api/inventories/summary) ou que le proxy Vite est actif."

export async function http<T = unknown>(
  path: string,
  opts: RequestInit & { timeoutMs?: number } = {},
): Promise<T> {
  const url = path.startsWith('http') ? path : `${API_BASE}${path}`
  const { timeoutMs, headers, body, signal, ...rest } = opts
  const controller = new AbortController()
  const timeout = setTimeout(() => controller.abort(), timeoutMs ?? 15000)

  if (signal) {
    if (signal.aborted) {
      controller.abort()
    } else {
      signal.addEventListener('abort', () => controller.abort(), { once: true })
    }
  }

  const requestHeaders = new Headers(headers ?? {})

  if (!requestHeaders.has('Accept')) {
    requestHeaders.set('Accept', 'application/json')
  }

  if (body !== undefined && !requestHeaders.has('Content-Type')) {
    requestHeaders.set('Content-Type', 'application/json')
  }

  if (authToken && !requestHeaders.has('Authorization')) {
    requestHeaders.set('Authorization', `Bearer ${authToken}`)
  }

  try {
    const response = await fetch(url, {
      ...rest,
      method: (rest.method ?? 'GET') as HttpMethod,
      headers: requestHeaders,
      cache: rest.cache ?? 'no-store',
      credentials: rest.credentials ?? 'include',
      mode: rest.mode ?? 'cors',
      body,
      signal: controller.signal,
    })

    if (!response.ok) {
      if (response.status === 401 && unauthorizedCallback) {
        unauthorizedCallback()
      }

      const rawText = await safeText(response)
      const text = truncate(rawText)
      const diagnostic = { url, status: response.status, body: text }
      const message =
        import.meta.env.DEV && response.status === 404
          ? DEV_API_UNREACHABLE_HINT
          : `HTTP ${response.status} ${response.statusText}`

      if (import.meta.env.DEV) {
        console.error('[http] error', diagnostic)
      }

      throw new HttpError(message, response.status, text, url)
    }

    return await parseResponse<T>(response)
  } catch (error) {
    if (error instanceof HttpError) {
      throw error
    }

    const isAbort = (error as { name?: string })?.name === 'AbortError'
    const fallbackMessage = (error as Error)?.message || 'NetworkError'
    const enriched = isAbort
      ? new HttpError('Timeout', 408, undefined, url)
      : new HttpError(fallbackMessage, undefined, undefined, url)

    if (import.meta.env.DEV) {
      console.error('[http] unexpected error', { url, error })
    }

    throw enriched
  } finally {
    clearTimeout(timeout)
  }
}

export const setAuthToken = (token: string | null) => {
  authToken = token
}

export const onUnauthorized = (callback: () => void) => {
  unauthorizedCallback = callback
}

async function parseResponse<T>(response: Response): Promise<T> {
  const contentType = response.headers.get('content-type') ?? ''
  if (!contentType.toLowerCase().includes('application/json')) {
    return (await safeText(response)) as unknown as T
  }

  try {
    return (await response.json()) as T
  } catch {
    return undefined as T
  }
}
