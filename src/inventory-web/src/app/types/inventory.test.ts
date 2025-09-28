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
      },
    ]

    const result = LocationsSchema.parse(sample)

    expect(result).toHaveLength(1)
    expect(result[0].activeStartedAtUtc).toBeInstanceOf(Date)
    expect(result[0].activeStartedAtUtc?.toISOString()).toBe('2025-09-28T16:04:34.514Z')
    expect(result[0].busyBy).toBeNull()
    expect(result[0].activeRunId).toBeNull()
    expect(result[0].activeCountType).toBeNull()
  })
})
