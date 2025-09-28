import { CountType, LocationsSchema } from '../../types/inventory'
import type { Location } from '../../types/inventory'

const RAW_DEV_LOCATIONS: Location[] = LocationsSchema.parse([
  {
    id: '11111111-1111-4111-8111-111111111111',
    code: 'B1',
    label: 'Zone B1 – Présentoir entrée',
    isBusy: true,
    busyBy: 'Alice',
    activeRunId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
    activeCountType: CountType.Count1,
    activeStartedAtUtc: new Date(Date.now() - 15 * 60_000).toISOString(),
    countStatuses: [
      {
        countType: CountType.Count1,
        status: 'in_progress',
        runId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
        operatorDisplayName: 'Alice',
        startedAtUtc: new Date(Date.now() - 15 * 60_000).toISOString(),
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
  {
    id: '22222222-2222-4222-8222-222222222222',
    code: 'B2',
    label: 'Zone B2 – Nouveautés Blu-ray',
    isBusy: true,
    busyBy: 'Bruno',
    activeRunId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
    activeCountType: CountType.Count2,
    activeStartedAtUtc: new Date(Date.now() - 45 * 60_000).toISOString(),
    countStatuses: [
      {
        countType: CountType.Count1,
        status: 'completed',
        runId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
        operatorDisplayName: 'Camille',
        startedAtUtc: new Date(Date.now() - 3 * 60 * 60_000).toISOString(),
        completedAtUtc: new Date(Date.now() - 2 * 60 * 60_000).toISOString(),
      },
      {
        countType: CountType.Count2,
        status: 'in_progress',
        runId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
        operatorDisplayName: 'Bruno',
        startedAtUtc: new Date(Date.now() - 45 * 60_000).toISOString(),
        completedAtUtc: null,
      },
    ],
  },
  {
    id: '33333333-3333-4333-8333-333333333333',
    code: 'B3',
    label: 'Zone B3 – Merchandising',
    isBusy: false,
    busyBy: null,
    activeRunId: null,
    activeCountType: null,
    activeStartedAtUtc: null,
    countStatuses: [
      {
        countType: CountType.Count1,
        status: 'completed',
        runId: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc',
        operatorDisplayName: 'Elisa',
        startedAtUtc: new Date(Date.now() - 24 * 60 * 60_000).toISOString(),
        completedAtUtc: new Date(Date.now() - 22 * 60 * 60_000).toISOString(),
      },
      {
        countType: CountType.Count2,
        status: 'completed',
        runId: 'dddddddd-dddd-4ddd-8ddd-dddddddddddd',
        operatorDisplayName: 'David',
        startedAtUtc: new Date(Date.now() - 20 * 60 * 60_000).toISOString(),
        completedAtUtc: new Date(Date.now() - 18 * 60 * 60_000).toISOString(),
      },
    ],
  },
  {
    id: '44444444-4444-4444-8444-444444444444',
    code: 'S1',
    label: 'Zone S1 – Réserve stock',
    isBusy: false,
    busyBy: null,
    activeRunId: null,
    activeCountType: null,
    activeStartedAtUtc: null,
    countStatuses: [
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
    ],
  },
])

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

