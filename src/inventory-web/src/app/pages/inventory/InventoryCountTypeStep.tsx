import { useCallback, useMemo } from 'react'
import { useNavigate } from 'react-router-dom'

import { Card } from '../../components/Card'
import { useInventory } from '../../contexts/InventoryContext'
import { CountType } from '../../types/inventory'
import type { LocationCountStatus } from '../../types/inventory'
import { getLocationDisplayName } from '../../utils/locationDisplay'

const DISPLAYED_COUNT_TYPES: CountType[] = [CountType.Count1, CountType.Count2]

const resolveOwnerNameForMessage = (
  status: LocationCountStatus | undefined,
  selectedUserId: string | null,
  selectedUserDisplayName: string | null,
): string | null => {
  if (!status) {
    return null
  }
  const ownerDisplayName = status.ownerDisplayName?.trim() ?? null
  const ownerUserId = status.ownerUserId?.trim() ?? null
  if (ownerUserId && selectedUserId && ownerUserId === selectedUserId) {
    return 'vous'
  }
  if (!ownerUserId && ownerDisplayName && selectedUserDisplayName && ownerDisplayName === selectedUserDisplayName) {
    return 'vous'
  }
  if (ownerDisplayName) {
    return ownerDisplayName
  }
  if (ownerUserId) {
    return 'un opérateur inconnu'
  }
  return 'un opérateur non identifié'
}

const isStatusOwnedByUser = (
  status: LocationCountStatus | undefined,
  selectedUserId: string | null,
  selectedUserDisplayName: string | null,
) => {
  if (!status) {
    return false
  }
  const ownerDisplayName = status.ownerDisplayName?.trim() ?? null
  const ownerUserId = status.ownerUserId?.trim() ?? null
  if (ownerUserId && selectedUserId) {
    return ownerUserId === selectedUserId
  }
  if (!ownerUserId && ownerDisplayName && selectedUserDisplayName) {
    return ownerDisplayName === selectedUserDisplayName
  }
  if (!ownerUserId && !ownerDisplayName) {
    return true
  }
  return false
}

export const InventoryCountTypeStep = () => {
  const navigate = useNavigate()
  const { selectedUser, location, sessionId, setCountType, setSessionId, clearSession } = useInventory()
  const selectedUserDisplayName = selectedUser?.displayName ?? null
  const selectedUserId = selectedUser?.id?.trim() ?? null

  const countStatuses = useMemo<LocationCountStatus[]>(() => {
    if (!location || !Array.isArray(location.countStatuses)) {
      return DISPLAYED_COUNT_TYPES.map<LocationCountStatus>((type) => ({
        countType: type,
        status: 'not_started',
        runId: null,
        ownerDisplayName: null,
        ownerUserId: null,
        startedAtUtc: null,
        completedAtUtc: null,
      }))
    }
    const byType = new Map<number, LocationCountStatus>()
    for (const status of location.countStatuses) {
      byType.set(status.countType, status)
    }
    return DISPLAYED_COUNT_TYPES.map<LocationCountStatus>((type) => {
      const existing = byType.get(type)
      if (existing) {
        return existing
      }
      return {
        countType: type,
        status: 'not_started',
        runId: null,
        ownerDisplayName: null,
        ownerUserId: null,
        startedAtUtc: null,
        completedAtUtc: null,
      }
    })
  }, [location])

  const zoneCompleted = useMemo(
    () => countStatuses.length > 0 && countStatuses.every((status) => status.status === 'completed'),
    [countStatuses],
  )

  const count1Status = useMemo(
    () => countStatuses.find((item) => item.countType === CountType.Count1),
    [countStatuses],
  )

  const canAccessSecondCount = useMemo(() => {
    if (!count1Status) {
      return false
    }
    return count1Status.status === 'completed'
  }, [count1Status])

  const hasUserCompletedOtherEligibleCount = useCallback(
    (type: CountType) =>
      DISPLAYED_COUNT_TYPES.filter((candidate) => candidate !== type).some((candidate) => {
        const otherStatus = countStatuses.find((item) => item.countType === candidate)
        if (otherStatus?.status !== 'completed') {
          return false
        }
        return resolveOwnerNameForMessage(otherStatus, selectedUserId, selectedUserDisplayName) === 'vous'
      }),
    [countStatuses, selectedUserDisplayName, selectedUserId],
  )

  const hasUserInProgressOtherEligibleCount = useCallback(
    (type: CountType) =>
      DISPLAYED_COUNT_TYPES.filter((candidate) => candidate !== type).some((candidate) => {
        const otherStatus = countStatuses.find((item) => item.countType === candidate)
        if (otherStatus?.status !== 'in_progress') {
          return false
        }
        return resolveOwnerNameForMessage(otherStatus, selectedUserId, selectedUserDisplayName) === 'vous'
      }),
    [countStatuses, selectedUserDisplayName, selectedUserId],
  )

  const displayName = location ? getLocationDisplayName(location.code, location.label) : ''

  const handleSelect = (type: CountType) => {
    const status = countStatuses.find((item) => item.countType === type)
    if (!status || zoneCompleted) {
      return
    }

    if (type === CountType.Count2 && !canAccessSecondCount) {
      return
    }

    if (status.status === 'completed') {
      return
    }

    if (
      (type === CountType.Count1 || type === CountType.Count2) &&
      (hasUserCompletedOtherEligibleCount(type) || hasUserInProgressOtherEligibleCount(type))
    ) {
      return
    }

    if (status.status === 'in_progress') {
      const statusOwnerId = status.ownerUserId?.trim() ?? null
      const statusOwnerName = status.ownerDisplayName?.trim() ?? null
      const isCurrentUser =
        statusOwnerId && selectedUserId
          ? statusOwnerId === selectedUserId
          : statusOwnerName === selectedUserDisplayName
      if (!isCurrentUser) {
        return
      }
    }

    setCountType(type)

    if (status.status === 'in_progress') {
      const isSameSession = sessionId === status.runId
      if (!isSameSession) {
        clearSession()
      }
      setSessionId(status.runId ?? null)
    } else {
      clearSession()
      setSessionId(null)
    }

    navigate('/inventory/session')
  }

  return (
    <div className="flex flex-col gap-6" data-testid="page-count-type">
      <Card className="flex flex-col gap-5 pb-4 sm:pb-5">
        <div className="flex flex-col gap-2">
          <p className="text-xs uppercase tracking-[0.2em] text-brand-500 dark:text-brand-200">Étape 3</p>
          <h2 className="text-2xl font-semibold text-slate-900 dark:text-white">
            Quel comptage souhaitez-vous lancer ?
          </h2>
          <p className="text-sm text-slate-500 dark:text-slate-400">
            {location
              ? `Choisissez le passage à réaliser pour la zone ${displayName}.`
              : 'Sélectionnez la zone à inventorier pour continuer.'}
          </p>
        </div>
        <div className="cards">
          {DISPLAYED_COUNT_TYPES.map((option) => {
            const status = countStatuses.find((item) => item.countType === option)
            const isCompleted = status?.status === 'completed'
            const isInProgress = status?.status === 'in_progress'
            const isOwnedByUser = isStatusOwnedByUser(status, selectedUserId, selectedUserDisplayName)
            const ownerNameForMessage = resolveOwnerNameForMessage(status, selectedUserId, selectedUserDisplayName)
            const isCompletedByUser = Boolean(isCompleted && ownerNameForMessage === 'vous')
            const isInProgressByUser = Boolean(isInProgress && isOwnedByUser)
            const isInProgressByOther = Boolean(isInProgress && !isOwnedByUser)
            const userHasCompletedOtherEligibleCount = hasUserCompletedOtherEligibleCount(option)
            const userHasInProgressOtherEligibleCount = hasUserInProgressOtherEligibleCount(option)
            const userHasClaimedOtherEligibleCount =
              userHasCompletedOtherEligibleCount || userHasInProgressOtherEligibleCount
            const isUserExcludedForThisCount =
              (option === CountType.Count1 || option === CountType.Count2) &&
              userHasClaimedOtherEligibleCount &&
              !(isCompletedByUser || isInProgressByUser)
            const isSequentiallyLocked = option === CountType.Count2 && !canAccessSecondCount
            const isDisabled =
              zoneCompleted || isCompleted || isInProgressByOther || isUserExcludedForThisCount || isSequentiallyLocked
            const helperMessage = (() => {
              if (zoneCompleted) {
                return 'Comptages terminés.'
              }
              if (isCompleted) {
                if (isCompletedByUser) {
                  return 'Vous avez déjà effectué ce comptage.'
                }
                const label = ownerNameForMessage ?? 'un opérateur inconnu'
                return `Comptage terminé par ${label}.`
              }
              if (isUserExcludedForThisCount) {
                if (userHasCompletedOtherEligibleCount) {
                  return 'Vous avez déjà effectué un autre comptage pour cette zone.'
                }
                return 'Vous avez déjà un autre comptage en cours pour cette zone.'
              }
              if (isSequentiallyLocked) {
                return 'Terminez d’abord le comptage n°1.'
              }
              if (isInProgressByOther) {
                const label = ownerNameForMessage ?? 'un autre opérateur'
                return `En cours par ${label}.`
              }
              if (isInProgressByUser) {
                return 'Reprenez votre comptage en cours.'
              }
              return 'Disponible.'
            })()
            return (
              <button
                key={option}
                type="button"
                onClick={() => handleSelect(option)}
                title={isDisabled ? helperMessage : undefined}
                className={`flex flex-col gap-2 rounded-3xl border px-6 py-6 text-left transition-all ${
                  isDisabled
                    ? 'cursor-not-allowed border-slate-200 bg-slate-100 text-slate-400 dark:border-slate-700 dark:bg-slate-900/60 dark:text-slate-500'
                    : 'border-slate-200 bg-white text-slate-800 hover:border-brand-400/40 hover:bg-brand-50 dark:border-slate-700 dark:bg-slate-900/40 dark:text-slate-200'
                }`}
                data-testid={option === CountType.Count1 ? 'btn-count-type-1' : 'btn-count-type-2'}
                disabled={isDisabled}
                aria-disabled={isDisabled}
              >
                <span className="text-4xl font-bold">Comptage n°{option}</span>
                <span className="text-sm text-slate-500 dark:text-slate-400">&nbsp;</span>
                <span className="text-xs font-medium text-slate-500 dark:text-slate-400">{helperMessage}</span>
              </button>
            )
          })}
        </div>
      </Card>
    </div>
  )
}
