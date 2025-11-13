import { render, screen, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import { useEffect } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { InventoryProvider, useInventory } from '../../../contexts/InventoryContext'
import type { InventorySummary, Location } from '../../../types/inventory'
import { InventoryLocationStep } from '../InventoryLocationStep'

import type { ShopUser } from '@/types/user'

const fetchLocationsMock = vi.hoisted(() => vi.fn())
const fetchInventorySummaryMock = vi.hoisted(() => vi.fn())

vi.mock('../../../api/inventoryApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../api/inventoryApi')>()
  return {
    ...actual,
    fetchLocations: fetchLocationsMock,
    fetchInventorySummary: fetchInventorySummaryMock,
  }
})

vi.mock('@/state/ShopContext', () => ({
  useShop: () => ({
    shop: { id: 'shop-test', name: 'Boutique test', kind: 'boutique' },
    setShop: vi.fn(),
    isLoaded: true,
  }),
}))

const operator: ShopUser = {
  id: '00000000-0000-4000-8000-000000000321',
  shopId: 'shop-test',
  login: 'operator',
  displayName: 'Opératrice Test',
  isAdmin: false,
  disabled: false,
}

const baseLocation: Location = {
  id: 'location-test',
  code: 'Z01',
  label: 'Zone test',
  isBusy: false,
  busyBy: null,
  activeRunId: null,
  activeCountType: null,
  activeStartedAtUtc: null,
  countStatuses: [
    {
      countType: 1,
      status: 'completed',
      runId: 'run-1',
      ownerDisplayName: operator.displayName,
      ownerUserId: operator.id,
      startedAtUtc: new Date('2025-01-01T10:00:00Z'),
      completedAtUtc: new Date('2025-01-01T11:00:00Z'),
    },
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
  disabled: false,
}

const summary: InventorySummary = {
  activeSessions: 0,
  openRuns: 0,
  completedRuns: 0,
  conflicts: 0,
  lastActivityUtc: null,
  openRunDetails: [],
  completedRunDetails: [],
  conflictZones: [],
}

const InventoryInitializer = ({ children }: { children: ReactNode }) => {
  const { setSelectedUser, clearSession } = useInventory()
  useEffect(() => {
    clearSession()
    setSelectedUser(operator)
  }, [clearSession, setSelectedUser])
  return <>{children}</>
}

describe('InventoryLocationStep', () => {
  beforeEach(() => {
    fetchLocationsMock.mockReset()
    fetchInventorySummaryMock.mockReset()
    fetchLocationsMock.mockResolvedValue([baseLocation])
    fetchInventorySummaryMock.mockResolvedValue(summary)
  })

  it('grise une zone déjà comptée par l’opérateur actif', async () => {
    render(
      <MemoryRouter initialEntries={[{ pathname: '/inventory/location' }]}> 
        <InventoryProvider>
          <InventoryInitializer>
            <InventoryLocationStep />
          </InventoryInitializer>
        </InventoryProvider>
      </MemoryRouter>,
    )

    await waitFor(() => expect(fetchLocationsMock).toHaveBeenCalled())

    const button = await screen.findByTestId('btn-select-zone')
    expect(button).toBeDisabled()
    expect(button).toHaveAttribute('title', 'Vous avez déjà compté cette zone.')
    expect(await screen.findByTestId('zone-restricted-message')).toHaveTextContent(
      'Vous avez déjà compté cette zone.',
    )
  })

  it("n'affiche pas les zones désactivées", async () => {
    const disabledLocation: Location = {
      ...baseLocation,
      id: 'disabled-zone',
      code: 'Z99',
      label: 'Zone désactivée',
      disabled: true,
      countStatuses: [],
    }
    fetchLocationsMock.mockResolvedValue([baseLocation, disabledLocation])

    render(
      <MemoryRouter initialEntries={[{ pathname: '/inventory/location' }]}>
        <InventoryProvider>
          <InventoryInitializer>
            <InventoryLocationStep />
          </InventoryInitializer>
        </InventoryProvider>
      </MemoryRouter>,
    )

    await waitFor(() => expect(fetchLocationsMock).toHaveBeenCalled())

    expect(screen.queryByTestId('zone-card-disabled-zone')).not.toBeInTheDocument()
    expect(screen.getAllByText(baseLocation.label)[0]).toBeInTheDocument()
  })
})
