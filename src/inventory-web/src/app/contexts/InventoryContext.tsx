import type { ReactNode } from 'react'
import { createContext, useContext, useMemo, useState } from 'react'
import type { CountType, InventoryItem, Location, Product } from '../types/inventory'

interface InventoryContextValue {
  selectedUser: string | null
  countType: CountType | null
  location: Location | null
  sessionId: string | null
  items: InventoryItem[]
  setSelectedUser: (user: string) => void
  setCountType: (type: CountType | null) => void
  setLocation: (location: Location) => void
  setSessionId: (sessionId: string | null) => void
  addOrIncrementItem: (product: Product, options?: { isManual?: boolean }) => void
  setQuantity: (ean: string, quantity: number) => void
  removeItem: (ean: string) => void
  reset: () => void
  clearSession: () => void
}

interface InventoryState {
  selectedUser: string | null
  countType: CountType | null
  location: Location | null
  sessionId: string | null
  items: InventoryItem[]
}

const INITIAL_STATE: InventoryState = {
  selectedUser: null,
  countType: null,
  location: null,
  sessionId: null,
  items: [],
}

const InventoryContext = createContext<InventoryContextValue | undefined>(undefined)

const createInventoryItemId = () => {
  if (typeof globalThis.crypto !== 'undefined' && typeof globalThis.crypto.randomUUID === 'function') {
    return globalThis.crypto.randomUUID()
  }

  const randomSuffix = Math.random().toString(36).slice(2)
  return `inventory-item-${Date.now().toString(36)}-${randomSuffix}`
}

export const InventoryProvider = ({ children }: { children: ReactNode }) => {
  const [state, setState] = useState<InventoryState>(INITIAL_STATE)

  const setSelectedUser = (user: string) => {
    setState(() => ({ ...INITIAL_STATE, selectedUser: user }))
  }

  const setCountType = (type: CountType | null) => {
    setState((prev) => ({ ...prev, countType: type }))
  }

  const setLocation = (location: Location) => {
    setState((prev) => ({ ...prev, location }))
  }

  const setSessionId = (sessionId: string | null) => {
    setState((prev) => ({ ...prev, sessionId }))
  }

  const addOrIncrementItem = (product: Product, options?: { isManual?: boolean }) => {
    setState((prev) => {
      const existingIndex = prev.items.findIndex((item) => item.product.ean === product.ean)
      if (existingIndex >= 0) {
        const nextItems = [...prev.items]
        const existing = nextItems[existingIndex]
        nextItems[existingIndex] = {
          ...existing,
          quantity: existing.quantity + 1,
          lastScanAt: new Date().toISOString(),
        }
        return { ...prev, items: nextItems }
      }
      const nextItem: InventoryItem = {
        id: createInventoryItemId(),
        product,
        quantity: 1,
        lastScanAt: new Date().toISOString(),
        isManual: Boolean(options?.isManual),
        addedAt: Date.now(),
      }
      return { ...prev, items: [...prev.items, nextItem] }
    })
  }

  const setQuantity = (ean: string, quantity: number) => {
    setState((prev) => ({
      ...prev,
      items: prev.items.map((item) =>
        item.product.ean === ean ? { ...item, quantity, lastScanAt: new Date().toISOString() } : item,
      ),
    }))
  }

  const removeItem = (ean: string) => {
    setState((prev) => ({
      ...prev,
      items: prev.items.filter((item) => item.product.ean !== ean),
    }))
  }

  const reset = () => setState(() => ({ ...INITIAL_STATE }))

  const clearSession = () =>
    setState((prev) => ({ ...prev, sessionId: null, items: [] }))

  const value = useMemo<InventoryContextValue>(
    () => ({
      selectedUser: state.selectedUser,
      countType: state.countType,
      location: state.location,
      sessionId: state.sessionId,
      items: state.items,
      setSelectedUser,
      setCountType,
      setLocation,
      setSessionId,
      addOrIncrementItem,
      setQuantity,
      removeItem,
      reset,
      clearSession,
    }),
    [state],
  )

  return <InventoryContext.Provider value={value}>{children}</InventoryContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export const useInventory = () => {
  const context = useContext(InventoryContext)
  if (!context) {
    throw new Error('useInventory doit être utilisé dans InventoryProvider')
  }
  return context
}
