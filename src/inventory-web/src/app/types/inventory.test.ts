import { describe, expect, it } from 'vitest'
import { LocationsSchema } from './inventory'

describe('LocationsSchema', () => {
  it('accepte les dates ISO avec microsecondes et les champs nullables', () => {
    const sample = [
      {
        id: '7e9c66e7-6a0f-4d2c-9dc1-9e541ca9f3f7',
        code: 'LOC-01',
        label: 'Réserve',
        isBusy: true,
        busyBy: null,
        activeRunId: null,
        activeCountType: null,
        activeStartedAtUtc: '2025-09-28T16:04:34.514414+00:00',
        countStatuses: [
          {
            countType: 1,
            status: 'completed',
            runId: 'a7b66fdd-3c9a-4f1f-9a4d-9a5a5cfdf111',
            ownerDisplayName: 'Alex',
            ownerUserId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
            startedAtUtc: '2025-09-27T09:00:00+00:00',
            completedAtUtc: '2025-09-27T09:30:00+00:00',
          },
          {
            countType: 2,
            status: 'not_started',
            runId: null,
            ownerDisplayName: null,
            ownerUserId: null,
            startedAtUtc: '',
            completedAtUtc: '',
          },
        ],
      },
    ]

    const result = LocationsSchema.parse(sample)

    expect(result).toHaveLength(1)
    expect(result[0].activeStartedAtUtc).toBeInstanceOf(Date)
    expect(result[0].activeStartedAtUtc?.toISOString()).toBe('2025-09-28T16:04:34.514Z')
    expect(result[0].busyBy).toBeNull()
    expect(result[0].activeRunId).toBeNull()
    expect(result[0].activeCountType).toBeNull()
    expect(result[0].countStatuses).toHaveLength(2)
    expect(result[0].countStatuses[0].status).toBe('completed')
    expect(result[0].countStatuses[1].status).toBe('not_started')
    expect(result[0].countStatuses[1]).toHaveProperty('ownerDisplayName', null)
    expect(result[0].countStatuses[1].startedAtUtc).toBeNull()
    expect(result[0].countStatuses[1].completedAtUtc).toBeNull()
  })

  it('normalise les UUID vides ou "null" en null dans les statuts', () => {
    const sample = [
      {
        id: '9c2c1c57-f1fd-4439-a024-73563f0ef6d3',
        code: 'LOC-02',
        label: 'Réserve 2',
        isBusy: false,
        busyBy: '  null  ',
        activeRunId: 'null',
        activeCountType: null,
        activeStartedAtUtc: null,
        countStatuses: [
          {
            countType: 1,
            status: 'not_started',
            runId: '',
            ownerDisplayName: null,
            ownerUserId: ' null ',
            startedAtUtc: null,
            completedAtUtc: null,
          },
        ],
      },
    ]

    const [location] = LocationsSchema.parse(sample)

    expect(location.activeRunId).toBeNull()
    expect(location.countStatuses[0]?.runId).toBeNull()
    expect(location.countStatuses[0]?.ownerUserId).toBeNull()
  })
})
