import { API_BASE } from './config'

type HttpMethod = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'

let authToken: string | null = null
let unauthorizedCallback: (() => void) | undefined

export class HttpError extends Error {
  constructor(
    message: string,
    public status?: number,
    public bodyText?: string,
    public url?: string,
  ) {
    super(message)
    this.name = 'HttpError'
  }
}

export async function http<T = unknown>(
  path: string,
  opts: RequestInit & { timeoutMs?: number } = {},
): Promise<T> {
  const url = path.startsWith('http') ? path : `${API_BASE}${path}`
  const controller = new AbortController()
  const timeout = setTimeout(() => controller.abort(), opts.timeoutMs ?? 15000)

  const { headers, cache, credentials, mode, method, body, ...rest } = opts

  const finalHeaders = new Headers(headers ?? {})
  if (!finalHeaders.has('Accept')) {
    finalHeaders.set('Accept', 'application/json')
  }
  if (body && !finalHeaders.has('Content-Type')) {
    finalHeaders.set('Content-Type', 'application/json')
  }
  if (authToken && !finalHeaders.has('Authorization')) {
    finalHeaders.set('Authorization', `Bearer ${authToken}`)
  }

  const requestInit: RequestInit = {
    ...rest,
    method: (method ?? 'GET') as HttpMethod,
    headers: finalHeaders,
    cache: cache ?? 'no-store',
    credentials: credentials ?? 'omit',
    mode: mode ?? 'cors',
    signal: controller.signal,
  }

  if (body !== undefined) {
    requestInit.body = body
  }

  try {
    const response = await fetch(url, requestInit)

    if (!response.ok) {
      if (response.status === 401 && unauthorizedCallback) {
        unauthorizedCallback()
      }
      const text = await safeText(response)
      throw new HttpError(`HTTP ${response.status}`, response.status, text, url)
    }

    return await safeJson<T>(response)
  } catch (error) {
    if (error instanceof HttpError) {
      throw error
    }
    const err =
      (error as { name?: string })?.name === 'AbortError'
        ? new HttpError('Timeout', 408, undefined, url)
        : new HttpError((error as Error)?.message || 'NetworkError', undefined, undefined, url)
    throw err
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

async function safeJson<T>(response: Response): Promise<T> {
  const contentType = response.headers.get('content-type') ?? ''
  if (!contentType.toLowerCase().includes('application/json')) {
    return undefined as T
  }
  try {
    return (await response.json()) as T
  } catch {
    return undefined as T
  }
}

async function safeText(response: Response): Promise<string | undefined> {
  try {
    return await response.text()
  } catch {
    return undefined
  }
}
