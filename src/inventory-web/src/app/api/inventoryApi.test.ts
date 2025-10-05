import { ZodError } from 'zod'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

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

describe('fetchLocations', () => {
  beforeEach(() => {
    vi.resetModules()
    vi.clearAllMocks()
  })

  afterEach(() => {
    vi.resetModules()
    vi.doUnmock('@/lib/api/http')
    vi.restoreAllMocks()
  })

  it('exige un identifiant de boutique non vide', async () => {
    const { fetchLocations } = await import('./inventoryApi')

    await expect(fetchLocations('')).rejects.toThrow('Aucune boutique sélectionnée.')
  })

  it('valide la réponse strictement et convertit les dates ISO', async () => {
    const httpMock = vi.fn().mockResolvedValue([
      {
        id: '00000000-0000-4000-8000-000000000001',
        code: 'Z1',
        label: 'Zone Z1',
        isBusy: false,
        busyBy: null,
        activeRunId: null,
        activeCountType: null,
        activeStartedAtUtc: '2025-01-01T09:00:00Z',
        countStatuses: [
          {
            countType: 1,
            status: 'in_progress',
            runId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
            ownerDisplayName: 'Alex',
            ownerUserId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
            startedAtUtc: '2025-01-01T09:30:00Z',
            completedAtUtc: null,
          },
        ],
      },
    ])
    mockHttpModule(httpMock)

    const { fetchLocations } = await import('./inventoryApi')

    const [location] = await fetchLocations(defaultShopId)

    expect(httpMock).toHaveBeenCalledWith(
      expect.stringContaining(`shopId=${encodeURIComponent(defaultShopId)}`),
    )
    expect(location.activeStartedAtUtc).toBeInstanceOf(Date)
    expect(location.countStatuses[0]?.startedAtUtc).toBeInstanceOf(Date)
    expect(location.countStatuses[0]?.completedAtUtc).toBeNull()
  })

  it('rejette la réponse quand le contrat n’est pas respecté', async () => {
    const httpMock = vi.fn().mockResolvedValue([
      {
        id: 'invalid',
        code: 'Z2',
        label: 'Zone Z2',
        isBusy: false,
        busyBy: null,
        activeRunId: null,
        activeCountType: null,
        activeStartedAtUtc: null,
        countStatuses: [],
      },
    ])
    mockHttpModule(httpMock)

    const { fetchLocations } = await import('./inventoryApi')

    await expect(fetchLocations(defaultShopId)).rejects.toBeInstanceOf(ZodError)
  })

  it('propage les erreurs HTTP sans fallback', async () => {
    const httpMock = vi.fn().mockRejectedValue(createHttpError(404))
    mockHttpModule(httpMock)

    const { fetchLocations } = await import('./inventoryApi')

    await expect(fetchLocations(defaultShopId)).rejects.toMatchObject({ status: 404 })
  })
})

describe('fetchLocationSummaries', () => {
  beforeEach(() => {
    vi.resetModules()
    vi.clearAllMocks()
  })

  afterEach(() => {
    vi.resetModules()
    vi.doUnmock('@/lib/api/http')
    vi.restoreAllMocks()
  })

  it('exige un identifiant de boutique non vide', async () => {
    const { fetchLocationSummaries } = await import('./inventoryApi')

    await expect(fetchLocationSummaries('')).rejects.toThrow('Aucune boutique sélectionnée.')
  })

  it('valide la réponse et convertit les dates ISO', async () => {
    const httpMock = vi.fn().mockResolvedValue([
      {
        locationId: '00000000-0000-4000-8000-000000000111',
        locationName: 'Zone principale',
        busyBy: null,
        activeRunId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        activeCountType: 1,
        activeStartedAtUtc: '2025-01-01T09:15:00Z',
        countStatuses: [
          {
            runId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
            startedAtUtc: '2025-01-01T09:15:00Z',
            completedAtUtc: null,
          },
          {
            runId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
            startedAtUtc: '2025-01-01T07:00:00Z',
            completedAtUtc: '2025-01-01T07:45:00Z',
          },
        ],
      },
    ])
    mockHttpModule(httpMock)

    const { fetchLocationSummaries } = await import('./inventoryApi')

    const [summary] = await fetchLocationSummaries(defaultShopId)

    expect(httpMock).toHaveBeenCalledWith(
      expect.stringContaining(`/inventory/locations/summary?shopId=${encodeURIComponent(defaultShopId)}`),
      expect.objectContaining({ signal: undefined }),
    )
    expect(summary.activeStartedAtUtc).toBeInstanceOf(Date)
    expect(summary.countStatuses[0]?.startedAtUtc).toBeInstanceOf(Date)
    expect(summary.countStatuses[0]?.completedAtUtc).toBeNull()
    expect(summary.countStatuses[1]?.completedAtUtc).toBeInstanceOf(Date)
  })

  it('retourne un tableau vide si la réponse brute ne fournit pas de liste', async () => {
    const httpMock = vi.fn().mockResolvedValue({ message: 'ok' })
    mockHttpModule(httpMock)

    const { fetchLocationSummaries } = await import('./inventoryApi')

    const summaries = await fetchLocationSummaries(defaultShopId)

    expect(Array.isArray(summaries)).toBe(true)
    expect(summaries).toHaveLength(0)
  })

  it('propage les erreurs HTTP', async () => {
    const httpMock = vi.fn().mockRejectedValue(createHttpError(500))
    mockHttpModule(httpMock)

    const { fetchLocationSummaries } = await import('./inventoryApi')

    await expect(fetchLocationSummaries(defaultShopId)).rejects.toMatchObject({ status: 500 })
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

