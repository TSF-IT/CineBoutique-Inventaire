import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import { useEffect, useLayoutEffect } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { InventoryProvider, useInventory } from '../../../contexts/InventoryContext'
import { InventorySessionPage } from '../InventorySessionPage'
import { CountType } from '../../../types/inventory'
import type { Location } from '../../../types/inventory'
import type { ShopUser } from '@/types/user'
import type { Shop } from '@/types/shop'

const getConflictZoneDetailMock = vi.hoisted(() => vi.fn())
const { shopMock } = vi.hoisted(() => ({
  shopMock: { id: 'shop-test', name: 'Boutique test' } as Shop,
}))

const inventoryControls: { setCountType?: (type: number | null) => void } = {}

vi.mock('../../../api/inventoryApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../api/inventoryApi')>()
  return {
    ...actual,
    getConflictZoneDetail: getConflictZoneDetailMock,
  }
})

vi.mock('@/state/ShopContext', () => ({
  useShop: () => ({ shop: shopMock, setShop: vi.fn(), isLoaded: true }),
}))

const owner: ShopUser = {
  id: 'user-test',
  shopId: shopMock.id,
  login: 'user.test',
  displayName: 'Utilisateur Test',
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
      runId: 'run-c1',
      ownerDisplayName: 'Utilisateur 1',
      ownerUserId: 'user-c1',
      startedAtUtc: null,
      completedAtUtc: null,
    },
    {
      countType: 2,
      status: 'completed',
      runId: 'run-c2',
      ownerDisplayName: 'Utilisateur 2',
      ownerUserId: 'user-c2',
      startedAtUtc: null,
      completedAtUtc: null,
    },
    {
      countType: 3,
      status: 'not_started',
      runId: null,
      ownerDisplayName: null,
      ownerUserId: null,
      startedAtUtc: null,
      completedAtUtc: null,
    },
  ],
}

const InventoryStateInitializer = ({
  children,
  location,
  countType,
}: {
  children: ReactNode
  location: Location
  countType: CountType
}) => {
  const { setSelectedUser, setLocation, setCountType, clearSession } = useInventory()
  useLayoutEffect(() => {
    clearSession()
    setSelectedUser(owner)
    setLocation(location)
    setCountType(countType)
  }, [clearSession, countType, location, setCountType, setLocation, setSelectedUser])

  useEffect(() => {
    setCountType(countType)
  }, [countType, setCountType])

  useEffect(() => {
    inventoryControls.setCountType = setCountType
    return () => {
      inventoryControls.setCountType = undefined
    }
  }, [setCountType])

  return <>{children}</>
}

const renderSessionPage = (countType: CountType) => {
  return render(
    <MemoryRouter initialEntries={[{ pathname: '/inventory/session' }]}> 
      <InventoryProvider>
        <InventoryStateInitializer location={baseLocation} countType={countType}>
          <InventorySessionPage />
        </InventoryStateInitializer>
      </InventoryProvider>
    </MemoryRouter>,
  )
}

describe('InventorySessionPage - conflits', () => {
  beforeEach(() => {
    getConflictZoneDetailMock.mockReset()
    getConflictZoneDetailMock.mockResolvedValue({
      locationId: baseLocation.id,
      locationCode: baseLocation.code,
      locationLabel: baseLocation.label,
      items: [],
    })
  })

  it('affiche un accès aux écarts pour un troisième comptage', async () => {
    renderSessionPage(CountType.Count3)

    expect(await screen.findByTestId('btn-view-conflicts')).toBeInTheDocument()
  })

  it('ouvre le détail des écarts sur demande', async () => {
    renderSessionPage(CountType.Count3)

    const [button] = await screen.findAllByTestId('btn-view-conflicts')
    fireEvent.click(button)

    await waitFor(() => expect(getConflictZoneDetailMock).toHaveBeenCalled())
    expect(
      await screen.findByRole('dialog', {
        name: `${baseLocation.code} · ${baseLocation.label}`,
      }),
    ).toBeInTheDocument()
  })
})
