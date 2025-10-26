import type { ShopUser } from '@/types/user'

export type InventoryHttpContextSnapshot = {
  selectedUser: ShopUser | null
  sessionId: string | null
}

const sanitizeSessionId = (value: string | null): string | null => {
  if (typeof value !== 'string') {
    return null
  }
  const trimmed = value.trim()
  return trimmed.length > 0 ? trimmed : null
}

const DEFAULT_HTTP_CONTEXT_SNAPSHOT: InventoryHttpContextSnapshot = {
  selectedUser: null,
  sessionId: null,
}

let currentHttpContextSnapshot: InventoryHttpContextSnapshot = { ...DEFAULT_HTTP_CONTEXT_SNAPSHOT }

export const syncInventoryHttpContextSnapshot = (snapshot: {
  selectedUser: ShopUser | null
  sessionId: string | null
}) => {
  currentHttpContextSnapshot = {
    selectedUser: snapshot.selectedUser,
    sessionId: sanitizeSessionId(snapshot.sessionId),
  }
}

export const resetInventoryHttpContextSnapshot = () => {
  currentHttpContextSnapshot = { ...DEFAULT_HTTP_CONTEXT_SNAPSHOT }
}

export const getInventoryHttpContextSnapshot = (): InventoryHttpContextSnapshot => ({
  selectedUser: currentHttpContextSnapshot.selectedUser,
  sessionId: currentHttpContextSnapshot.sessionId,
})

export const __setInventoryHttpContextSnapshotForTests = (
  snapshot: InventoryHttpContextSnapshot,
) => {
  syncInventoryHttpContextSnapshot(snapshot)
}
