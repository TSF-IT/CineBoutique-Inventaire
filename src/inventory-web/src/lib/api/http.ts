// src/lib/api/http.ts
import { loadShop } from '@/lib/shopStorage'

export interface HttpError extends Error {
  status: number
  url: string
  body?: string
  problem?: unknown
}

const SNIPPET_MAX_LENGTH = 512

const truncateSnippet = (value: string) =>
  value.length <= SNIPPET_MAX_LENGTH ? value : value.slice(0, SNIPPET_MAX_LENGTH)

const buildHttpError = (
  message: string,
  res: Response,
  bodyText: string,
  problem?: unknown,
): HttpError =>
  Object.assign(new Error(message), {
    status: res.status ?? 0,
    url: res.url,
    body: truncateSnippet(bodyText),
    problem,
  })

export async function http(input: string, init: RequestInit = {}) {
  const headers = new Headers(init.headers)
  const shop = loadShop()
  if (shop?.id) {
    headers.set('X-Shop-Id', shop.id)
  }

  const res = await fetch(input, {
    ...init,
    headers,
  })
  if (!res.ok) {
    const bodyText = await res.text().catch(() => '')
    const err: HttpError = buildHttpError(`HTTP ${res.status}`, res, bodyText, (() => {
      try { return JSON.parse(bodyText) } catch { return undefined }
    })())
    throw err
  }

  const contentType = res.headers.get('content-type') ?? ''
  const text = await res.text()
  const snippet = truncateSnippet(text)

  if (!contentType.toLowerCase().startsWith('application/json')) {
    throw buildHttpError(
      `Réponse non JSON (content-type: ${contentType || 'inconnu'})`,
      res,
      text,
      {
        contentType,
        snippet,
      },
    )
  }

  try {
    return JSON.parse(text)
  } catch {
    throw buildHttpError('JSON invalide', res, text, {
      contentType,
      snippet,
    })
  }
}

export default http
