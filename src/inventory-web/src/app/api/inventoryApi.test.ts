import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { CompleteInventoryRunPayload } from './inventoryApi'

const mockHttpModule = (implementation: (...args: unknown[]) => unknown) => {
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
    vi.stubEnv('DEV', true)
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

    expect(locations).toHaveLength(39)
    expect(locations[0].code).toBe('B1')
    expect(locations[locations.length - 1].code).toBe('S19')
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

  it('normalise les identifiants de run vides en null', async () => {
    const httpMock = vi.fn().mockResolvedValue([
      {
        id: '00000000-0000-4000-8000-000000000001',
        code: 'Z1',
        label: 'Zone Z1',
        isBusy: false,
        busyBy: null,
        activeRunId: '   ',
        activeCountType: null,
        activeStartedAtUtc: null,
        countStatuses: [
          {
            countType: 1,
            status: 'not_started',
            runId: '',
            operatorDisplayName: null,
            startedAtUtc: null,
            completedAtUtc: null,
          },
        ],
      },
    ])
    mockHttpModule(httpMock)

    const { fetchLocations } = await import('./inventoryApi')

    const [location] = await fetchLocations()

    expect(location.activeRunId).toBeNull()
    expect(location.countStatuses[0]?.runId).toBeNull()
  })
})

describe('completeInventoryRun (dev fixtures)', () => {
  const originalFetch = global.fetch

  beforeEach(() => {
    vi.resetModules()
    vi.clearAllMocks()
    vi.stubEnv('DEV', true)
    vi.stubEnv('VITE_DISABLE_DEV_FIXTURES', '')
  })

  afterEach(() => {
    vi.resetModules()
    vi.unstubAllEnvs()
    vi.doUnmock('@/lib/api/http')
    vi.restoreAllMocks()
    global.fetch = originalFetch
  })

  it('retourne une réponse synthétique quand la zone existe dans les fixtures', async () => {
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const response = new Response('Not Found', { status: 404 })
    const fetchMock = vi.fn().mockResolvedValue(response)
    global.fetch = fetchMock as typeof global.fetch

    const { completeInventoryRun } = await import('./inventoryApi')

    const payload: CompleteInventoryRunPayload = {
      runId: 'existing-run-id',
      countType: 1,
      operator: 'Testeur',
      items: [{ ean: '1234567890123', quantity: 2, isManual: false }],
    }

    const result = await completeInventoryRun('44444444-4444-4444-8444-444444444444', payload)

    expect(result).toMatchObject({
      runId: 'existing-run-id',
      locationId: '44444444-4444-4444-8444-444444444444',
      countType: 1,
      itemsCount: 1,
      totalQuantity: 2,
    })
    expect(fetchMock).toHaveBeenCalled()
    expect(warnSpy).toHaveBeenCalled()
  })

  it('remonte une erreur 404 quand les fixtures sont désactivées', async () => {
    vi.stubEnv('VITE_DISABLE_DEV_FIXTURES', 'true')
    const response = new Response('Not Found', { status: 404 })
    const fetchMock = vi.fn().mockResolvedValue(response)
    global.fetch = fetchMock as typeof global.fetch

    const { completeInventoryRun } = await import('./inventoryApi')

    await expect(
      completeInventoryRun('44444444-4444-4444-8444-444444444444', {
        countType: 1,
        operator: 'Testeur',
        items: [{ ean: '1234567890123', quantity: 1, isManual: false }],
        runId: null,
      }),
    ).rejects.toMatchObject({
      message: 'Zone introuvable pour ce comptage.',
      status: 404,
    })
  })
})

