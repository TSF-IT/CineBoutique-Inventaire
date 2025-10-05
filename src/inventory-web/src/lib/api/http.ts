// src/inventory-web/src/lib/api/http.ts
import { loadShop } from '@/lib/shopStorage'

export interface HttpError extends Error {
  status: number
  url: string
  body?: string
  problem?: unknown
}

const SNIPPET_MAX_LENGTH = 512
const truncateSnippet = (v: string) => (v.length <= SNIPPET_MAX_LENGTH ? v : v.slice(0, SNIPPET_MAX_LENGTH))

function buildHttpError(message: string, res: Response, body?: string, extra?: Record<string, unknown>): HttpError {
  const err = new Error(message) as HttpError
  err.status = res.status
  err.url = res.url
  if (body != null) err.body = truncateSnippet(body)
  if (extra && 'problem' in extra) err.problem = (extra as { problem?: unknown }).problem
  return err
}

export type HttpRequestInit<TBody = unknown> = Omit<RequestInit, 'body'> & {
  body?: RequestInit['body'] | TBody
}

const isPlainObject = (value: unknown): value is Record<string, unknown> => {
  if (Object.prototype.toString.call(value) !== '[object Object]') {
    return false
  }
  const prototype = Object.getPrototypeOf(value)
  return prototype === null || prototype === Object.prototype
}

const shouldJsonStringify = (value: unknown): value is Record<string, unknown> | unknown[] => {
  if (!value || typeof value !== 'object') {
    return false
  }

  if (Array.isArray(value)) {
    return true
  }

  if (value instanceof FormData || value instanceof Blob) {
    return false
  }

  if (typeof URLSearchParams !== 'undefined' && value instanceof URLSearchParams) {
    return false
  }

  if (typeof ReadableStream !== 'undefined' && value instanceof ReadableStream) {
    return false
  }

  if (value instanceof ArrayBuffer) {
    return false
  }

  if (ArrayBuffer.isView(value)) {
    return false
  }

  return isPlainObject(value)
}

export default async function http<TBody = unknown>(
  url: string,
  init: HttpRequestInit<TBody> = {},
): Promise<unknown> {
  // 1) Construire l’URL et injecter shopId si applicable
  let finalUrl = url
  try {
    const u = new URL(url, window.location.origin)
    const path = u.pathname
    const isApi = path.startsWith('/api')
    const isShops = path.startsWith('/api/shops')

    const shop = loadShop()
    if (isApi && !isShops && shop?.id && !u.searchParams.has('shopId')) {
      u.searchParams.set('shopId', shop.id)
    }
    finalUrl = u.toString()
  } catch {
    // Si l’URL est relative “bizarre”, on laisse passer tel quel.
  }

  // 2) Préparer les headers et le body
  const headers = new Headers(init.headers ?? {})
  const rawBody = init.body
  let body: BodyInit | null | undefined = rawBody as BodyInit | null | undefined

  if (shouldJsonStringify(rawBody)) {
    if (!headers.has('Content-Type')) {
      headers.set('Content-Type', 'application/json')
    }
    body = JSON.stringify(rawBody)
  }

  const res = await fetch(finalUrl, { ...init, headers, body })

  const contentType = res.headers.get('Content-Type') ?? ''
  const isJson = contentType.includes('application/json')
  const text = await res.text()
  const snippet = truncateSnippet(text)

  if (!res.ok) {
    // Essayer d’extraire un “problem+json” pour enrichir l’erreur
    let problem: unknown = undefined
    if (isJson && text) {
      try {
        problem = JSON.parse(text)
      } catch {
        // rien
      }
    }
    const defaultMsg = `HTTP ${res.status}`
    const detail =
      (problem as { detail?: string } | undefined)?.detail ||
      (problem as { title?: string } | undefined)?.title ||
      snippet ||
      defaultMsg

    throw buildHttpError(detail, res, text, { problem, contentType, snippet })
  }

  if (!text) return null
  if (!isJson) return text

  try {
    return JSON.parse(text)
  } catch {
    throw buildHttpError('JSON invalide', res, text, {
      problem: { contentType, snippet },
      contentType,
      snippet,
    })
  }
}
