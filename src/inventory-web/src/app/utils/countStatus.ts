import type { LocationCountStatus } from '../types/inventory'

export interface StatusOwnerInfo {
  label: string | null
  isCurrentUser: boolean
}

const normalize = (value: string | null | undefined): string | null => {
  if (typeof value !== 'string') {
    return null
  }
  const trimmed = value.trim()
  return trimmed.length > 0 ? trimmed : null
}

export const resolveStatusOwnerInfo = (
  status: LocationCountStatus,
  selectedUserId: string | null,
  selectedUserDisplayName: string | null,
): StatusOwnerInfo => {
  const ownerId = normalize(status.ownerUserId)
  const ownerDisplayName = normalize(status.ownerDisplayName)
  const currentUserId = normalize(selectedUserId)
  const currentUserDisplayName = normalize(selectedUserDisplayName)

  let isCurrentUser = false

  if (ownerId && currentUserId) {
    isCurrentUser = ownerId === currentUserId
  } else if (ownerDisplayName && currentUserDisplayName) {
    isCurrentUser = ownerDisplayName === currentUserDisplayName
  }

  return {
    label: isCurrentUser ? 'vous' : ownerDisplayName,
    isCurrentUser,
  }
}

export const formatOwnerSuffix = (
  status: LocationCountStatus,
  selectedUserId: string | null,
  selectedUserDisplayName: string | null,
  options?: { fallback?: string },
): string | null => {
  const owner = resolveStatusOwnerInfo(status, selectedUserId, selectedUserDisplayName)
  if (owner.label) {
    return `par ${owner.label}`
  }
  if (options?.fallback) {
    return `par ${options.fallback}`
  }
  return null
}

export const isStatusOwnedByCurrentUser = (
  status: LocationCountStatus,
  selectedUserId: string | null,
  selectedUserDisplayName: string | null,
): boolean => resolveStatusOwnerInfo(status, selectedUserId, selectedUserDisplayName).isCurrentUser
