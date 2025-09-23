import { API_BASE } from './config'

type HttpMethod = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'

export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  [key: string]: unknown
}

interface HttpErrorOptions {
  message: string
  status?: number
  url?: string
  body?: string
  problem?: ProblemDetails
  cause?: unknown
}

let authToken: string | null = null
let unauthorizedCallback: (() => void) | undefined

export class HttpError extends Error {
  status?: number
  body?: string
  url?: string
  problem?: ProblemDetails

  constructor({ message, status, body, url, problem, cause }: HttpErrorOptions) {
    super(message)
    this.name = 'HttpError'
    this.status = status
    this.body = body
    this.url = url
    this.problem = problem
    if (cause !== undefined) {
      ;(this as Error & { cause?: unknown }).cause = cause
    }
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

const parseProblem = (body: string | undefined): ProblemDetails | undefined => {
  if (!body) {
    return undefined
  }
  try {
    const parsed = JSON.parse(body)
    return typeof parsed === 'object' && parsed !== null ? (parsed as ProblemDetails) : undefined
  } catch {
    return undefined
  }
}

const buildErrorMessage = (response: Response, problem?: ProblemDetails, body?: string): string => {
  const statusLabel = response.statusText?.trim()
  let message = `HTTP ${response.status}${statusLabel ? ` ${statusLabel}` : ''}`
  if (problem?.title) {
    message = `${message} – ${problem.title}`
  } else if (problem?.detail) {
    message = `${message} – ${problem.detail}`
  } else if (body) {
    message = `${message} – see body`
  }
  return message
}

export const DEV_API_UNREACHABLE_HINT =
  "Impossible de joindre l’API : vérifie que le backend tourne (curl http://localhost:8080/healthz) ou que le proxy Vite est actif."

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

      const rawBody = await safeText(response)
      const bodyForMessage = truncate(rawBody)
      const problem = parseProblem(rawBody)
      const message = buildErrorMessage(response, problem, bodyForMessage)

      console.error('[http] error', { url, status: response.status, body: rawBody })

      throw new HttpError({
        message,
        status: response.status,
        body: rawBody,
        url,
        problem,
      })
    }

    return await parseResponse<T>(response)
  } catch (error) {
    if (error instanceof HttpError) {
      throw error
    }

    const isAbort = (error as { name?: string })?.name === 'AbortError'
    const fallbackMessage = (error as Error)?.message || 'NetworkError'
    const enriched = isAbort
      ? new HttpError({ message: 'Timeout', status: 408, url })
      : new HttpError({ message: fallbackMessage, url, cause: error })

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
