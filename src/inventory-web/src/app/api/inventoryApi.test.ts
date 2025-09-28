import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const mockHttpModule = (implementation: (...args: any[]) => unknown) => {
  vi.doMock('@/lib/api/http', () => ({
    default: implementation,
  }))
}

const createHttpError = (status: number, url = '/api/locations') =>
  Object.assign(new Error(`HTTP ${status}`), {
    status,
    url,
  })

describe('fetchLocations (dev fixtures)', () => {
  beforeEach(() => {
    vi.resetModules()
    vi.clearAllMocks()
    vi.stubEnv('DEV', 'true')
    vi.stubEnv('VITE_DISABLE_DEV_FIXTURES', '')
  })

  afterEach(() => {
    vi.resetModules()
    vi.unstubAllEnvs()
    vi.doUnmock('@/lib/api/http')
    vi.restoreAllMocks()
  })

  it('retourne les fixtures quand l’API répond 500', async () => {
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const httpMock = vi.fn().mockRejectedValue(createHttpError(500))
    mockHttpModule(httpMock)

    const { fetchLocations } = await import('./inventoryApi')

    const locations = await fetchLocations()

    expect(locations).toHaveLength(4)
    expect(locations[0].code).toBe('B1')
    expect(warnSpy).toHaveBeenCalled()
  })

  it('remonte l’erreur HTTP 404 sans fallback', async () => {
    const httpMock = vi.fn().mockRejectedValue(createHttpError(404))
    mockHttpModule(httpMock)

    const { fetchLocations } = await import('./inventoryApi')

    await expect(fetchLocations()).rejects.toMatchObject({ status: 404 })
  })

  it('désactive le fallback quand VITE_DISABLE_DEV_FIXTURES=true', async () => {
    vi.stubEnv('VITE_DISABLE_DEV_FIXTURES', 'true')
    const httpMock = vi.fn().mockRejectedValue(createHttpError(500))
    mockHttpModule(httpMock)

    const { fetchLocations } = await import('./inventoryApi')

    await expect(fetchLocations()).rejects.toMatchObject({ status: 500 })
  })
})

