import { CountType, LocationsSchema } from '../../types/inventory'
import type { Location } from '../../types/inventory'

const minutesAgo = (minutes: number): Date => new Date(Date.now() - minutes * 60_000)

const createDefaultStatuses = (): Location['countStatuses'] => [
  {
    countType: CountType.Count1,
    status: 'not_started',
    runId: null,
    operatorDisplayName: null,
    startedAtUtc: null,
    completedAtUtc: null,
  },
  {
    countType: CountType.Count2,
    status: 'not_started',
    runId: null,
    operatorDisplayName: null,
    startedAtUtc: null,
    completedAtUtc: null,
  },
]

const buildLocation = (
  code: string,
  index: number,
  overrides?: Partial<Location>,
): Location => {
  const base: Location = {
    id: `00000000-0000-4000-8000-${(index + 1).toString(16).padStart(12, '0')}`,
    code,
    label: `Zone ${code}`,
    isBusy: false,
    busyBy: null,
    activeRunId: null,
    activeCountType: null,
    activeStartedAtUtc: null,
    countStatuses: createDefaultStatuses(),
  }

  if (!overrides) {
    return base
  }

  return {
    ...base,
    ...overrides,
    label: overrides.label ?? base.label,
    countStatuses: (overrides.countStatuses ?? base.countStatuses).map((status) => ({
      ...status,
      startedAtUtc: status.startedAtUtc ?? null,
      completedAtUtc: status.completedAtUtc ?? null,
    })),
  }
}

const B_CODES = Array.from({ length: 20 }, (_, index) => `B${index + 1}`)
const S_CODES = Array.from({ length: 19 }, (_, index) => `S${index + 1}`)

const specialOverrides: Record<string, Partial<Location>> = {
  B1: {
    id: '11111111-1111-4111-8111-111111111111',
    label: 'Zone B1',
    isBusy: true,
    busyBy: 'Alice',
    activeRunId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
    activeCountType: CountType.Count1,
    activeStartedAtUtc: minutesAgo(15),
    countStatuses: [
      {
        countType: CountType.Count1,
        status: 'in_progress',
        runId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        operatorDisplayName: 'Alice',
        startedAtUtc: minutesAgo(15),
        completedAtUtc: null,
      },
      {
        countType: CountType.Count2,
        status: 'not_started',
        runId: null,
        operatorDisplayName: null,
        startedAtUtc: null,
        completedAtUtc: null,
      },
    ],
  },
  B2: {
    id: '22222222-2222-4222-8222-222222222222',
    label: 'Zone B2',
    isBusy: true,
    busyBy: 'Bruno',
    activeRunId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
    activeCountType: CountType.Count2,
    activeStartedAtUtc: minutesAgo(45),
    countStatuses: [
      {
        countType: CountType.Count1,
        status: 'completed',
        runId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
        operatorDisplayName: 'Camille',
        startedAtUtc: minutesAgo(180),
        completedAtUtc: minutesAgo(120),
      },
      {
        countType: CountType.Count2,
        status: 'in_progress',
        runId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
        operatorDisplayName: 'Bruno',
        startedAtUtc: minutesAgo(45),
        completedAtUtc: null,
      },
    ],
  },
  B3: {
    id: '33333333-3333-4333-8333-333333333333',
    label: 'Zone B3',
    countStatuses: [
      {
        countType: CountType.Count1,
        status: 'completed',
        runId: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc',
        operatorDisplayName: 'Elisa',
        startedAtUtc: minutesAgo(24 * 60),
        completedAtUtc: minutesAgo(22 * 60),
      },
      {
        countType: CountType.Count2,
        status: 'completed',
        runId: 'dddddddd-dddd-4ddd-8ddd-dddddddddddd',
        operatorDisplayName: 'David',
        startedAtUtc: minutesAgo(20 * 60),
        completedAtUtc: minutesAgo(18 * 60),
      },
    ],
  },
  B4: {
    id: '44444444-4444-4444-8444-444444444444',
    label: 'Zone B4',
  },
  S1: {
    id: '55555555-5555-4555-8555-555555555555',
    label: 'Zone S1',
  },
}

const RAW_DEV_LOCATIONS: Location[] = LocationsSchema.parse(
  [...B_CODES, ...S_CODES].map((code, index) => buildLocation(code, index, specialOverrides[code])),
)

export const DEV_LOCATIONS_FIXTURE: readonly Location[] = RAW_DEV_LOCATIONS

export const areDevFixturesEnabled = (): boolean =>
  import.meta.env.DEV && import.meta.env.VITE_DISABLE_DEV_FIXTURES !== 'true'

export const cloneDevLocations = (): Location[] =>
  DEV_LOCATIONS_FIXTURE.map((location) => ({
    ...location,
    activeStartedAtUtc: location.activeStartedAtUtc
      ? new Date(location.activeStartedAtUtc)
      : location.activeStartedAtUtc,
    countStatuses: location.countStatuses.map((status) => ({
      ...status,
      startedAtUtc: status.startedAtUtc ? new Date(status.startedAtUtc) : status.startedAtUtc,
      completedAtUtc: status.completedAtUtc ? new Date(status.completedAtUtc) : status.completedAtUtc,
    })),
  }))

