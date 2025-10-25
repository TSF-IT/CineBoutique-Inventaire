import { describe, expect, it } from 'vitest'

import { LocationsSchema } from './inventory'

const buildCountStatusPayload = (overrides: Record<string, unknown> = {}) => {
  const baseStatus = {
    countType: 1,
    status: 'completed',
    runId: 'a7b66fdd-3c9a-4f1f-9a4d-9a5a5cfdf111',
    ownerDisplayName: 'Alex',
    ownerUserId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
    startedAtUtc: '2025-09-27T09:00:00+00:00',
    completedAtUtc: '2025-09-27T09:30:00+00:00',
  }

  return {
    ...baseStatus,
    ...overrides,
  }
}

const buildLocationPayload = (overrides: Record<string, unknown> = {}) => {
  const baseLocation = {
    id: '7e9c66e7-6a0f-4d2c-9dc1-9e541ca9f3f7',
    code: 'LOC-01',
    label: 'Réserve',
    isBusy: true,
    busyBy: null,
    activeRunId: null,
    activeCountType: null,
    activeStartedAtUtc: '2025-09-28T16:04:34.514414+00:00',
    countStatuses: [
      buildCountStatusPayload(),
      {
        countType: 2,
        status: 'not_started',
        runId: null,
        ownerDisplayName: null,
        ownerUserId: null,
        startedAtUtc: null,
        completedAtUtc: null,
      },
    ],
  }

  const hasCustomStatuses = Object.prototype.hasOwnProperty.call(overrides, 'countStatuses')

  return {
    ...baseLocation,
    ...overrides,
    countStatuses: hasCustomStatuses ? overrides.countStatuses : baseLocation.countStatuses,
  }
}

describe('LocationsSchema', () => {
  it('convertit les dates ISO valides et normalise les valeurs nullish', () => {
    const sample = [buildLocationPayload()]

    const result = LocationsSchema.parse(sample)

    expect(result).toHaveLength(1)
    expect(result[0].activeStartedAtUtc).toBeInstanceOf(Date)
    expect(result[0].activeStartedAtUtc?.toISOString()).toBe('2025-09-28T16:04:34.514Z')
    expect(result[0].countStatuses).toHaveLength(2)
    expect(result[0].countStatuses[0].startedAtUtc).toBeInstanceOf(Date)
    expect(result[0].countStatuses[0].completedAtUtc).toBeInstanceOf(Date)
    expect(result[0].countStatuses[1].startedAtUtc).toBeNull()
    expect(result[0].countStatuses[1].completedAtUtc).toBeNull()
  })

  it('accepte les champs undefined et les convertit en valeurs sûres', () => {
    const sample = [
      buildLocationPayload({
        busyBy: undefined,
        activeRunId: undefined,
        activeCountType: undefined,
        activeStartedAtUtc: undefined,
        countStatuses: [
          buildCountStatusPayload({
            runId: undefined,
            ownerDisplayName: undefined,
            ownerUserId: undefined,
            startedAtUtc: undefined,
            completedAtUtc: undefined,
          }),
        ],
      }),
    ]

    const [location] = LocationsSchema.parse(sample)

    expect(location.busyBy).toBeNull()
    expect(location.activeRunId).toBeNull()
    expect(location.activeCountType).toBeNull()
    expect(location.activeStartedAtUtc).toBeNull()
    expect(location.countStatuses).toHaveLength(1)
    expect(location.countStatuses[0]).toMatchObject({
      runId: null,
      ownerDisplayName: null,
      ownerUserId: null,
      startedAtUtc: null,
      completedAtUtc: null,
    })
  })

  it('retourne un tableau vide quand les statuts sont absents', () => {
    const sample = [
      buildLocationPayload({ countStatuses: undefined }),
    ]

    const [location] = LocationsSchema.parse(sample)

    expect(location.countStatuses).toEqual([])
  })

  it('rejette toute valeur incompatible avec le contrat (UUID et dates)', () => {
    const invalidSample = [
      {
        ...buildLocationPayload(),
        id: 'not-a-uuid',
        activeRunId: 'null',
        activeCountType: 4,
        activeStartedAtUtc: 'not-a-date',
        countStatuses: [
          {
            ...buildCountStatusPayload({ status: 'not_started', runId: '', ownerDisplayName: null }),
            ownerUserId: ' null ',
            startedAtUtc: 'not-a-date',
            completedAtUtc: null,
          },
        ],
      },
    ]

    expect(() => LocationsSchema.parse(invalidSample)).toThrowError()
  })

  it('rejette un ownerUserId qui ne respecte pas le format UUID', () => {
    const invalidSample = [
      buildLocationPayload({
        countStatuses: [
          buildCountStatusPayload({ ownerUserId: 'owner-42' }),
        ],
      }),
    ]

    expect(() => LocationsSchema.parse(invalidSample)).toThrowError()
  })

  it("rejette un statut d'inventaire inconnu", () => {
    const invalidSample = [
      buildLocationPayload({
        countStatuses: [
          buildCountStatusPayload({ status: 'pending' }),
        ],
      }),
    ]

    expect(() => LocationsSchema.parse(invalidSample)).toThrowError()
  })

  it('convertit les dates impossibles à interpréter en null', () => {
    const sample = [
      buildLocationPayload({
        countStatuses: [
          buildCountStatusPayload({ startedAtUtc: 'date-non-valide', completedAtUtc: 'date-non-valide' }),
        ],
      }),
    ]

    const [location] = LocationsSchema.parse(sample)

    expect(location.countStatuses[0]).toMatchObject({
      startedAtUtc: null,
      completedAtUtc: null,
    })
  })
})
