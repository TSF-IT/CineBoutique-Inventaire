// src/lib/api/http.ts
export interface HttpError extends Error {
  status: number
  url: string
  body?: string
  problem?: unknown
}

export async function http(input: string, init?: RequestInit) {
  const res = await fetch(input, init)
  if (!res.ok) {
    const bodyText = await res.text().catch(() => '')
    const err: HttpError = Object.assign(new Error(`HTTP ${res.status}`), {
      status: res.status,
      url: res.url,
      body: bodyText,
      // Essaie de parser en JSON si possible
      problem: (() => {
        try { return JSON.parse(bodyText) } catch { return undefined }
      })()
    })
    throw err
  }
  // tente JSON sinon texte
  const text = await res.text()
  try { return JSON.parse(text) } catch { return text }
}

export default http
