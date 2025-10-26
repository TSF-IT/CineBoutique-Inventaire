import type { ReactNode } from 'react'
import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'

import type { InventoryItem, InventoryLogEntry, InventoryLogEventType, Location, Product } from '../types/inventory'

import type { ShopUser } from '@/types/user'

export interface InventoryContextValue {
  selectedUser: ShopUser | null
  countType: number | null
  location: Location | null
  sessionId: string | null
  items: InventoryItem[]
  logs: InventoryLogEntry[]
  setSelectedUser: (user: ShopUser) => void
  setCountType: (type: number | null) => void
  setLocation: (location: Location) => void
  setSessionId: (sessionId: string | null) => void
  addOrIncrementItem: (product: Product, options?: { isManual?: boolean }) => void
  initializeItems: (
    entries: Array<{ product: Product; quantity?: number; isManual?: boolean; hasConflict?: boolean }>,
  ) => void
  setQuantity: (ean: string, quantity: number) => void
  removeItem: (ean: string) => void
  reset: () => void
  clearSession: () => void
  logEvent: (entry: { type: InventoryLogEventType; message: string; context?: InventoryLogEntry['context'] }) => void
  clearLogs: () => void
}

interface InventoryState {
  selectedUser: ShopUser | null
  countType: number | null
  location: Location | null
  sessionId: string | null
  items: InventoryItem[]
  logs: InventoryLogEntry[]
}

const INITIAL_STATE: InventoryState = {
  selectedUser: null,
  countType: null,
  location: null,
  sessionId: null,
  items: [],
  logs: [],
}

const InventoryContext = createContext<InventoryContextValue | undefined>(undefined)

const sanitizeSessionId = (value: string | null): string | null => {
  if (typeof value !== 'string') {
    return null
  }
  const trimmed = value.trim()
  return trimmed.length > 0 ? trimmed : null
}

export type InventoryHttpContextSnapshot = {
  selectedUser: ShopUser | null
  sessionId: string | null
}

const DEFAULT_HTTP_CONTEXT_SNAPSHOT: InventoryHttpContextSnapshot = {
  selectedUser: null,
  sessionId: null,
}

let currentHttpContextSnapshot: InventoryHttpContextSnapshot = { ...DEFAULT_HTTP_CONTEXT_SNAPSHOT }

const syncHttpContextSnapshot = (state: InventoryState) => {
  currentHttpContextSnapshot = {
    selectedUser: state.selectedUser,
    sessionId: sanitizeSessionId(state.sessionId),
  }
}

export const getInventoryHttpContextSnapshot = (): InventoryHttpContextSnapshot => ({
  selectedUser: currentHttpContextSnapshot.selectedUser,
  sessionId: currentHttpContextSnapshot.sessionId,
})

export const __setInventoryHttpContextSnapshotForTests = (
  snapshot: InventoryHttpContextSnapshot,
) => {
  currentHttpContextSnapshot = {
    selectedUser: snapshot.selectedUser,
    sessionId: sanitizeSessionId(snapshot.sessionId),
  }
}

const createInventoryItemId = () => {
  if (typeof globalThis.crypto !== 'undefined' && typeof globalThis.crypto.randomUUID === 'function') {
    return globalThis.crypto.randomUUID()
  }

  const randomSuffix = Math.random().toString(36).slice(2)
  return `inventory-item-${Date.now().toString(36)}-${randomSuffix}`
}

const createLogEntryId = () => {
  if (typeof globalThis.crypto !== 'undefined' && typeof globalThis.crypto.randomUUID === 'function') {
    return globalThis.crypto.randomUUID()
  }

  const randomSuffix = Math.random().toString(36).slice(2)
  return `inventory-log-${Date.now().toString(36)}-${randomSuffix}`
}

const prependLogEntry = (
  logs: InventoryLogEntry[],
  entry: { type: InventoryLogEventType; message: string; context?: InventoryLogEntry['context'] },
) => [{ id: createLogEntryId(), timestamp: new Date().toISOString(), ...entry }, ...logs]

export const InventoryProvider = ({ children }: { children: ReactNode }) => {
  const [state, setState] = useState<InventoryState>(INITIAL_STATE)

  useEffect(() => {
    syncHttpContextSnapshot(state)
  }, [state])

  useEffect(
    () => () => {
      syncHttpContextSnapshot(INITIAL_STATE)
    },
    [],
  )

  const setSelectedUser = useCallback((user: ShopUser) => {
    setState(() => ({ ...INITIAL_STATE, selectedUser: user }))
  }, [])

  const setCountType = useCallback((type: number | null) => {
    setState((prev) => ({ ...prev, countType: type }))
  }, [])

  const setLocation = useCallback((location: Location) => {
    setState((prev) => ({ ...prev, location }))
  }, [])

  const setSessionId = useCallback((sessionId: string | null) => {
    setState((prev) => ({ ...prev, sessionId }))
  }, [])

  const addOrIncrementItem = useCallback((product: Product, options?: { isManual?: boolean }) => {
    setState((prev) => {
      const existingIndex = prev.items.findIndex((item) => item.product.ean === product.ean)
      const timestamp = new Date().toISOString()
      if (existingIndex >= 0) {
        const existing = prev.items[existingIndex]
        const updated: InventoryItem = {
          ...existing,
          quantity: existing.quantity + 1,
          lastScanAt: timestamp,
          isManual: existing.isManual || Boolean(options?.isManual),
        }
        const remaining = prev.items.filter((_, index) => index !== existingIndex)
        return {
          ...prev,
          items: [updated, ...remaining],
          logs: prependLogEntry(prev.logs, {
            type: 'item-incremented',
            message: `Quantité augmentée pour ${updated.product.name} (EAN ${updated.product.ean}) → ${updated.quantity}`,
            context: {
              ean: updated.product.ean,
              productName: updated.product.name,
              quantity: updated.quantity,
              isManual: updated.isManual,
            },
          }),
        }
      }
      const nextItem: InventoryItem = {
        id: createInventoryItemId(),
        product,
        quantity: 1,
        lastScanAt: timestamp,
        isManual: Boolean(options?.isManual),
        addedAt: Date.now(),
      }
      return {
        ...prev,
        items: [nextItem, ...prev.items],
        logs: prependLogEntry(prev.logs, {
          type: 'item-added',
          message: `${product.name} ${options?.isManual ? 'ajouté manuellement' : 'ajouté à la session'} (EAN ${product.ean})`,
          context: {
            ean: product.ean,
            productName: product.name,
            quantity: nextItem.quantity,
            isManual: nextItem.isManual,
          },
        }),
      }
    })
  }, [])

  const initializeItems = useCallback<InventoryContextValue['initializeItems']>((entries) => {
    setState((prev) => {
      const now = new Date()
      const baseTimestamp = now.getTime()
      const isoTimestamp = now.toISOString()
      const items: InventoryItem[] = entries.map((entry, index) => ({
        id: createInventoryItemId(),
        product: entry.product,
        quantity: typeof entry.quantity === 'number' ? entry.quantity : 0,
        lastScanAt: isoTimestamp,
        isManual: Boolean(entry.isManual),
        addedAt: baseTimestamp + index,
        hasConflict: entry.hasConflict,
      }))

      return {
        ...prev,
        items,
        logs: prev.logs,
      }
    })
  }, [])

  const setQuantity = useCallback((ean: string, quantity: number) => {
    setState((prev) => {
      const index = prev.items.findIndex((item) => item.product.ean === ean)
      if (index < 0) {
        return prev
      }

      const existing = prev.items[index]
      const timestamp = new Date().toISOString()
      const updated: InventoryItem = {
        ...existing,
        quantity,
        lastScanAt: timestamp,
      }

      const remainingItems = prev.items.filter((_, itemIndex) => itemIndex !== index)

      return {
        ...prev,
        items: [updated, ...remainingItems],
        logs: prependLogEntry(prev.logs, {
          type: 'item-quantity-updated',
          message: `Quantité mise à jour pour ${updated.product.name} (EAN ${updated.product.ean}) → ${quantity}`,
          context: {
            ean: updated.product.ean,
            productName: updated.product.name,
            quantity,
            isManual: updated.isManual,
          },
        }),
      }
    })
  }, [])

  const removeItem = useCallback((ean: string) => {
    setState((prev) => {
      const index = prev.items.findIndex((item) => item.product.ean === ean)
      if (index < 0) {
        return prev
      }

      const item = prev.items[index]
      const remaining = prev.items.filter((_, itemIndex) => itemIndex !== index)
      return {
        ...prev,
        items: remaining,
        logs: prependLogEntry(prev.logs, {
          type: 'item-removed',
          message: `${item.product.name} retiré de la session (EAN ${item.product.ean})`,
          context: {
            ean: item.product.ean,
            productName: item.product.name,
            quantity: item.quantity,
            isManual: item.isManual,
          },
        }),
      }
    })
  }, [])

  const reset = useCallback(() => setState(() => ({ ...INITIAL_STATE })), [])

  const clearSession = useCallback(
    () => setState((prev) => ({ ...prev, sessionId: null, items: [], logs: [] })),
    [],
  )

  const logEvent = useCallback<InventoryContextValue['logEvent']>((entry) => {
    setState((prev) => ({
      ...prev,
      logs: prependLogEntry(prev.logs, entry),
    }))
  }, [])

  const clearLogs = useCallback(() => setState((prev) => ({ ...prev, logs: [] })), [])

  const value = useMemo<InventoryContextValue>(
    () => ({
      selectedUser: state.selectedUser,
      countType: state.countType,
      location: state.location,
      sessionId: state.sessionId,
      items: state.items,
      logs: state.logs,
      setSelectedUser,
      setCountType,
      setLocation,
      setSessionId,
      addOrIncrementItem,
      initializeItems,
      setQuantity,
      removeItem,
      reset,
      clearSession,
      logEvent,
      clearLogs,
    }),
    [
      state,
      addOrIncrementItem,
      clearLogs,
      clearSession,
      logEvent,
      removeItem,
      reset,
      setCountType,
      setLocation,
      initializeItems,
      setQuantity,
      setSelectedUser,
      setSessionId,
    ],
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
