import { render } from '@testing-library/react'
import type { ReactElement, ReactNode } from 'react'
import { MemoryRouter, useLocation, type Location } from 'react-router-dom'
import { useEffect, useRef } from 'react'

import { AppProviders } from '@/app/providers/AppProviders'
import type { Shop } from '@/types/shop'
import type {
  InventoryItem,
  InventoryLogEntry,
  InventoryLogEventType,
  Location as InventoryLocation,
} from '@/app/types/inventory'
import type { ShopUser } from '@/types/user'
import { useShop } from '@/state/ShopContext'
import { useInventory } from '@/app/contexts/InventoryContext'

interface InventoryItemInput {
  product: InventoryItem['product']
  quantity?: number
  isManual?: boolean
}

interface InventoryLogInput {
  type: InventoryLogEventType
  message: string
  context?: InventoryLogEntry['context']
}

interface InventoryTestState {
  selectedUser?: ShopUser
  countType?: number | null
  location?: InventoryLocation | null
  sessionId?: string | null
  items?: InventoryItemInput[]
  logs?: InventoryLogInput[]
}

interface RenderWithProvidersOptions {
  route?: string
  initialEntries?: string[]
  shop?: Shop | null
  inventory?: InventoryTestState
  captureHistory?: boolean
  childrenWrapper?: (children: ReactNode) => ReactElement
}

interface RenderWithProvidersResult extends ReturnType<typeof render> {
  history: Location[]
}

const ShopStateInitializer = ({ shop }: { shop: Shop | null | undefined }) => {
  const { setShop, isLoaded } = useShop()
  const appliedRef = useRef(false)

  useEffect(() => {
    if (!isLoaded || appliedRef.current) {
      return
    }

    appliedRef.current = true
    setShop(shop ?? null)
  }, [isLoaded, setShop, shop])

  return null
}

const InventoryStateInitializer = ({ state }: { state: InventoryTestState | undefined }) => {
  const {
    clearSession,
    setSelectedUser,
    setCountType,
    setLocation,
    setSessionId,
    addOrIncrementItem,
    setQuantity,
    logEvent,
  } = useInventory()

  const appliedRef = useRef(false)

  useEffect(() => {
    if (!state || appliedRef.current) {
      return
    }

    appliedRef.current = true
    clearSession()

    if (state.selectedUser) {
      setSelectedUser(state.selectedUser)
    }

    if (state.countType !== undefined) {
      setCountType(state.countType ?? null)
    }

    if (state.location) {
      setLocation(state.location)
    }

    if (state.sessionId !== undefined) {
      setSessionId(state.sessionId ?? null)
    }

    if (state.items?.length) {
      for (const item of state.items) {
        addOrIncrementItem(item.product, { isManual: item.isManual })
        if (item.quantity && item.quantity > 1) {
          setQuantity(item.product.ean, item.quantity)
        }
      }
    }

    if (state.logs?.length) {
      for (const entry of state.logs) {
        logEvent(entry)
      }
    }
  }, [
    addOrIncrementItem,
    clearSession,
    logEvent,
    setCountType,
    setLocation,
    setQuantity,
    setSelectedUser,
    setSessionId,
    state,
  ])

  return null
}

export const renderWithProviders = (
  ui: ReactElement,
  {
    route = '/',
    initialEntries,
    shop = null,
    inventory,
    captureHistory = false,
    childrenWrapper,
  }: RenderWithProvidersOptions = {},
): RenderWithProvidersResult => {
  const locations: Location[] = []

  const HistoryObserver = () => {
    const location = useLocation()
    useEffect(() => {
      if (captureHistory) {
        locations.push(location)
      }
    }, [location])
    return null
  }

  const Wrapper = ({ children }: { children: ReactNode }) => {
    const content = (
      <AppProviders>
        <ShopStateInitializer shop={shop} />
        <InventoryStateInitializer state={inventory} />
        {children}
      </AppProviders>
    )

    return childrenWrapper ? childrenWrapper(content) : content
  }

  const result = render(
    <MemoryRouter initialEntries={initialEntries ?? [route]}>
      {captureHistory ? <HistoryObserver /> : null}
      {ui}
    </MemoryRouter>,
    { wrapper: Wrapper },
  )

  return Object.assign(result, { history: locations })
}

export type { InventoryTestState, InventoryItemInput, InventoryLogInput }
