import { describe, expect, it, vi, afterEach } from 'vitest'

import { __setInventoryHttpContextSnapshotForTests } from '@/app/contexts/InventoryContext'
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
  sessionStorage.clear()
  __setInventoryHttpContextSnapshotForTests({ selectedUser: null, sessionId: null })
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

describe('http headers', () => {
  const originalAppToken = (import.meta.env as { VITE_APP_TOKEN?: string }).VITE_APP_TOKEN

  afterEach(() => {
    ;(import.meta.env as { VITE_APP_TOKEN?: string }).VITE_APP_TOKEN = originalAppToken
  })

  it("ajoute l'entête X-App-Token lorsque configuré", async () => {
    ;(import.meta.env as { VITE_APP_TOKEN?: string }).VITE_APP_TOKEN = 'secret-token'

    const fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(mockResponse(JSON.stringify({ ok: true }), { headers: { 'Content-Type': 'application/json' } }))

    await http(API_URL)

    expect(fetchSpy).toHaveBeenCalledTimes(1)
    const [, init] = fetchSpy.mock.calls[0] ?? []
    const headers = new Headers((init as RequestInit | undefined)?.headers ?? {})
    expect(headers.get('X-App-Token')).toBe('secret-token')
  })

  it("enrichit les entêtes opérateur et admin depuis le stockage", async () => {
    localStorage.setItem('cb.shop', JSON.stringify({ id: 'shop-999', name: 'Shop démo', kind: 'boutique' }))
    sessionStorage.setItem(
      'cb.inventory.selectedUser.shop-999',
      JSON.stringify({
        userId: '11111111-1111-1111-1111-111111111111',
        displayName: 'Alice Demo',
        login: 'alice@example.com',
        shopId: 'shop-999',
        isAdmin: true,
        disabled: false,
      }),
    )

    const fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(mockResponse(JSON.stringify({ ok: true }), { headers: { 'Content-Type': 'application/json' } }))

    await http(API_URL)

    const [, init] = fetchSpy.mock.calls[0] ?? []
    const headers = new Headers((init as RequestInit | undefined)?.headers ?? {})

    expect(headers.get('X-Operator-Id')).toBe('11111111-1111-1111-1111-111111111111')
    expect(headers.get('X-Operator-Name')).toBe('Alice Demo')
    expect(headers.get('X-Operator-Login')).toBe('alice@example.com')
    expect(headers.get('X-Operator-ShopId')).toBe('shop-999')
    expect(headers.get('X-Admin')).toBe('true')
  })

  it("ajoute l'identifiant de session courant quand disponible", async () => {
    __setInventoryHttpContextSnapshotForTests({ selectedUser: null, sessionId: 'session-abc' })

    const fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(mockResponse(JSON.stringify({ ok: true }), { headers: { 'Content-Type': 'application/json' } }))

    await http(API_URL)

    const [, init] = fetchSpy.mock.calls[0] ?? []
    const headers = new Headers((init as RequestInit | undefined)?.headers ?? {})

    expect(headers.get('X-Session-Id')).toBe('session-abc')
  })
})
