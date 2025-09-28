import { describe, expect, it, vi, afterEach } from 'vitest'
import { http, type HttpError } from './http'

const RESPONSE_URL = 'http://example.com/test'

const mockResponse = (body: string, options: { status?: number; headers?: Record<string, string> } = {}) => {
  const { status = 200, headers = {} } = options
  const response = new Response(body, {
    status,
    headers,
  })
  Object.defineProperty(response, 'url', { value: RESPONSE_URL })
  return response
}

afterEach(() => {
  vi.restoreAllMocks()
})

describe('http helper', () => {
  it('retourne le JSON quand la réponse est valide', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(
      mockResponse(JSON.stringify({ foo: 'bar' }), { headers: { 'Content-Type': 'application/json' } }),
    )

    const result = await http(RESPONSE_URL)

    expect(result).toEqual({ foo: 'bar' })
  })

  it('lève une HttpError quand le content-type est non JSON', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(
      mockResponse('<html>oops</html>', { headers: { 'Content-Type': 'text/html' } }),
    )

    const expected: Partial<HttpError> = {
      message: expect.stringContaining('Réponse non JSON') as unknown as string,
      problem: expect.objectContaining({
        contentType: 'text/html',
        snippet: expect.stringContaining('<html>'),
      }) as unknown,
    }

    await expect(http(RESPONSE_URL)).rejects.toMatchObject(expected)
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

    await expect(http(RESPONSE_URL)).rejects.toMatchObject(expected)
  })
})
