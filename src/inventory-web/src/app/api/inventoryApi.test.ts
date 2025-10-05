import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { CountType } from '../types/inventory'
import type { CompleteInventoryRunPayload } from './inventoryApi'

const mockHttpModule = (implementation: (...args: unknown[]) => unknown) => {
  vi.doMock('@/lib/api/http', () => ({
    default: implementation,
  }))
}

const createHttpError = (
  status: number,
  url = '/api/locations?shopId=11111111-1111-1111-1111-111111111111',
) =>
  Object.assign(new Error(`HTTP ${status}`), {
    status,
    url,
  })

const defaultShopId = '11111111-1111-1111-1111-111111111111'

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

    const locations = await fetchLocations(defaultShopId)

    expect(locations).toHaveLength(39)
    expect(locations[0].code).toBe('B1')
    expect(locations[locations.length - 1].code).toBe('S19')
    expect(warnSpy).toHaveBeenCalled()
  })

  it('retourne les fixtures quand l’API répond 404', async () => {
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const httpMock = vi.fn().mockRejectedValue(createHttpError(404))
    mockHttpModule(httpMock)

    const { fetchLocations } = await import('./inventoryApi')

    const locations = await fetchLocations(defaultShopId)

    expect(locations).toHaveLength(39)
    expect(locations[0].code).toBe('B1')
    expect(locations[locations.length - 1].code).toBe('S19')
    expect(warnSpy).toHaveBeenCalled()
  })

  it('désactive le fallback quand VITE_DISABLE_DEV_FIXTURES=true', async () => {
    vi.stubEnv('VITE_DISABLE_DEV_FIXTURES', 'true')
    const httpMock = vi.fn().mockRejectedValue(createHttpError(500))
    mockHttpModule(httpMock)

    const { fetchLocations } = await import('./inventoryApi')

    await expect(fetchLocations(defaultShopId)).rejects.toMatchObject({ status: 500 })
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
            ownerDisplayName: null,
            ownerUserId: null,
            startedAtUtc: null,
            completedAtUtc: null,
          },
        ],
      },
    ])
    mockHttpModule(httpMock)

    const { fetchLocations } = await import('./inventoryApi')

    const [location] = await fetchLocations(defaultShopId)

    expect(location.activeRunId).toBeNull()
    expect(location.countStatuses[0]?.runId).toBeNull()
  })

  it('lève une HttpError 422 avec les détails Zod quand la validation échoue', async () => {
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const httpMock = vi.fn().mockResolvedValue([
      {
        id: '00000000-0000-4000-8000-000000000001',
        code: 'Z1',
        label: null,
      },
    ])
    mockHttpModule(httpMock)

    const { fetchLocations } = await import('./inventoryApi')

    await expect(fetchLocations(defaultShopId)).rejects.toMatchObject({
      status: 422,
      problem: expect.objectContaining({
        fieldErrors: expect.any(Object),
      }),
    })
    expect(warnSpy).toHaveBeenCalled()
  })
})

describe('fetchLocations (parsing)', () => {
  beforeEach(() => {
    vi.resetModules()
    vi.clearAllMocks()
    vi.stubEnv('DEV', false)
  })

  afterEach(() => {
    vi.resetModules()
    vi.unstubAllEnvs()
    vi.doUnmock('@/lib/api/http')
    vi.restoreAllMocks()
  })

  it('normalise CountStatuses PascalCase et leurs clés internes', async () => {
    const httpMock = vi.fn().mockResolvedValue([
      {
        Id: '00000000-0000-4000-8000-000000000010',
        Code: 'Z2',
        Label: 'Zone Z2',
        IsBusy: false,
        BusyBy: null,
        ActiveRunId: null,
        ActiveCountType: 1,
        ActiveStartedAtUtc: null,
        CountStatuses: [
          {
            CountType: 1,
            Status: 'in_progress',
            RunId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
            OwnerDisplayName: 'Alex',
            OwnerUserId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
            StartedAtUtc: '2025-01-01T10:00:00Z',
            CompletedAtUtc: null,
          },
        ],
      },
    ])
    mockHttpModule(httpMock)

    const { fetchLocations } = await import('./inventoryApi')

    const [location] = await fetchLocations(defaultShopId)

    expect(location.code).toBe('Z2')
    expect(location.countStatuses).toHaveLength(1)
    expect(location.countStatuses[0]).toMatchObject({
      countType: CountType.Count1,
      status: 'in_progress',
      runId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
      ownerDisplayName: 'Alex',
      ownerUserId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
    })
    expect(location.countStatuses[0]?.startedAtUtc).toBeInstanceOf(Date)
  })

  it("injecte des statuts par défaut quand countStatuses est absent", async () => {
    const httpMock = vi.fn().mockResolvedValue([
      {
        id: '00000000-0000-4000-8000-000000000020',
        code: 'Z3',
        label: 'Zone Z3',
        isBusy: false,
        busyBy: null,
        activeRunId: null,
        activeCountType: null,
        activeStartedAtUtc: null,
      },
    ])
    mockHttpModule(httpMock)

    const { fetchLocations } = await import('./inventoryApi')

    const [location] = await fetchLocations(defaultShopId)

    expect(location.countStatuses).toHaveLength(2)
    expect(location.countStatuses.map((s) => s.countType)).toEqual([
      CountType.Count1,
      CountType.Count2,
    ])
    expect(location.countStatuses.every((s) => s.status === 'not_started')).toBe(true)
    expect(location.countStatuses.every((s) => s.runId === null)).toBe(true)
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
      ownerUserId: '00000000-0000-0000-0000-000000000001',
      countType: 1,
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
        ownerUserId: '00000000-0000-0000-0000-000000000001',
        items: [{ ean: '1234567890123', quantity: 1, isManual: false }],
        runId: null,
      }),
    ).rejects.toMatchObject({
      message: 'Zone introuvable pour ce comptage.',
      status: 404,
    })
  })
})

describe('startInventoryRun', () => {
  beforeEach(() => {
    vi.resetModules()
    vi.clearAllMocks()
  })

  afterEach(() => {
    vi.doUnmock('@/lib/api/http')
    vi.restoreAllMocks()
  })

  it('envoie la requête POST attendue', async () => {
    const httpMock = vi.fn().mockResolvedValue({
      runId: 'run-1',
      inventorySessionId: 'session-1',
      locationId: 'loc-1',
      countType: 1,
      ownerDisplayName: 'Utilisateur Paris',
      ownerUserId: '00000000-0000-0000-0000-000000000001',
      startedAtUtc: new Date().toISOString(),
    })
    mockHttpModule(httpMock)

    const { startInventoryRun } = await import('./inventoryApi')

    const result = await startInventoryRun('loc-1', {
      shopId: 'shop-1',
      ownerUserId: 'user-1',
      countType: 1,
    })

    expect(result).toMatchObject({ runId: 'run-1', countType: 1 })
    expect(httpMock).toHaveBeenCalledWith(
      expect.stringContaining('/inventories/loc-1/start'),
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ shopId: 'shop-1', ownerUserId: 'user-1', countType: 1 }),
      }),
    )
  })
})

describe('releaseInventoryRun', () => {
  const originalFetch = global.fetch

  beforeEach(() => {
    vi.resetModules()
    vi.clearAllMocks()
  })

  afterEach(() => {
    global.fetch = originalFetch
    vi.restoreAllMocks()
  })

  it('retourne void quand le backend répond 204', async () => {
    const response = new Response(null, { status: 204 })
    const fetchMock = vi.fn().mockResolvedValue(response)
    global.fetch = fetchMock as typeof global.fetch

    const { releaseInventoryRun } = await import('./inventoryApi')

    await expect(releaseInventoryRun('loc-1', 'run-1', '00000000-0000-0000-0000-000000000001')).resolves.toBeUndefined()
    const [calledUrl, rawOptions] = fetchMock.mock.calls[0] ?? []
    const options = (rawOptions ?? {}) as RequestInit

    expect(calledUrl).toContain('/inventories/loc-1/release')
    expect(options.method).toBe('POST')
    expect(options.body).toBe(
      JSON.stringify({ runId: 'run-1', ownerUserId: '00000000-0000-0000-0000-000000000001' }),
    )

    const headers =
      options.headers instanceof Headers ? options.headers : new Headers(options.headers ?? undefined)
    expect(headers.get('Content-Type')).toBe('application/json')
  })

  it('remonte le message de conflit retourné par le backend', async () => {
    const payload = JSON.stringify({ message: 'Comptage détenu par Paul.' })
    const response = new Response(payload, {
      status: 409,
      headers: { 'Content-Type': 'application/json' },
    })
    const fetchMock = vi.fn().mockResolvedValue(response)
    global.fetch = fetchMock as typeof global.fetch

    const { releaseInventoryRun } = await import('./inventoryApi')

    await expect(releaseInventoryRun('loc-1', 'run-1', '00000000-0000-0000-0000-000000000001')).rejects.toMatchObject({
      message: 'Comptage détenu par Paul.',
      status: 409,
    })
  })
})

