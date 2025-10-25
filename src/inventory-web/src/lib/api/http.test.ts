import { describe, expect, it, vi, afterEach } from 'vitest'

import http, { type HttpError } from './http'

const API_URL = '/api/test'

const mockResponse = (body: string, options: { status?: number; headers?: Record<string, string> } = {}) => {
  const { status = 200, headers = {} } = options
  const response = new Response(body, {
    status,
    headers,
  })
  Object.defineProperty(response, 'url', { value: API_URL })
  return response
}

afterEach(() => {
  vi.restoreAllMocks()
  localStorage.clear()
})

describe('http helper', () => {
  it('retourne le JSON quand la réponse est valide', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(
      mockResponse(JSON.stringify({ foo: 'bar' }), { headers: { 'Content-Type': 'application/json' } }),
    )

    const result = await http(API_URL)

    expect(result).toEqual({ foo: 'bar' })
  })

  it("ajoute l'identifiant de boutique dans la requête quand une boutique est sélectionnée", async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(
      mockResponse(JSON.stringify({ foo: 'bar' }), { headers: { 'Content-Type': 'application/json' } }),
    )

    localStorage.setItem('cb.shop', JSON.stringify({ id: 'shop-123', name: 'Shop démo', kind: 'boutique' }))

    await http(API_URL)

    const [calledUrl] = fetchSpy.mock.calls[0] ?? []
    const parsedUrl = typeof calledUrl === 'string' ? new URL(calledUrl, window.location.origin) : null

    expect(parsedUrl?.searchParams.get('shopId')).toBe('shop-123')
  })

  it('retourne le texte brut quand la réponse est non JSON', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(
      mockResponse('<html>oops</html>', { headers: { 'Content-Type': 'text/html' } }),
    )

    const result = await http(API_URL)

    expect(result).toBe('<html>oops</html>')
  })

  it('lève une HttpError quand le JSON est invalide', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(
      mockResponse('not-json', { headers: { 'Content-Type': 'application/json' } }),
    )

    const expected: Partial<HttpError> = {
      message: 'JSON invalide',
      problem: expect.objectContaining({
        contentType: 'application/json',
        snippet: 'not-json',
      }) as unknown,
    }

    await expect(http(API_URL)).rejects.toMatchObject(expected)
  })
})
