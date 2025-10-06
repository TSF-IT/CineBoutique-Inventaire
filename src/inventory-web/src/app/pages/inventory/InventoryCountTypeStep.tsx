import { useMemo } from 'react'
import { useNavigate } from 'react-router-dom'
import { Button } from '../../components/ui/Button'
import { Card } from '../../components/Card'
import { useInventory } from '../../contexts/InventoryContext'
import { CountType } from '../../types/inventory'
import type { LocationCountStatus } from '../../types/inventory'
import { getLocationDisplayName, isLocationLabelRedundant } from '../../utils/locationDisplay'
import { formatOwnerSuffix, isStatusOwnedByCurrentUser, resolveStatusOwnerInfo } from '../../utils/countStatus'

const DISPLAYED_COUNT_TYPES: CountType[] = [CountType.Count1, CountType.Count2]

const computeDurationLabel = (startedAtUtc: string | Date | null | undefined) => {
  if (!startedAtUtc) {
    return null
  }
  const started = startedAtUtc instanceof Date ? startedAtUtc : new Date(startedAtUtc)
  if (Number.isNaN(started.getTime())) {
    return null
  }
  const diffMs = Date.now() - started.getTime()
  if (diffMs < 0) {
    return null
  }
  const totalMinutes = Math.floor(diffMs / 60000)
  if (totalMinutes <= 0) {
    return "depuis moins d'1 min"
  }
  if (totalMinutes < 60) {
    return `depuis ${totalMinutes} min`
  }
  const hours = Math.floor(totalMinutes / 60)
  const minutes = totalMinutes % 60
  if (minutes === 0) {
    return `depuis ${hours} h`
  }
  return `depuis ${hours} h ${minutes} min`
}

const statusTextClass = (status: LocationCountStatus) => {
  if (status.status === 'completed') {
    return 'text-sm font-medium text-emerald-700 dark:text-emerald-200'
  }
  if (status.status === 'in_progress') {
    return 'text-sm font-medium text-amber-700 dark:text-amber-200'
  }
  return 'text-sm text-slate-600 dark:text-slate-300'
}

const statusIcon = (status: LocationCountStatus) => {
  if (status.status === 'completed') {
    return '✅'
  }
  if (status.status === 'in_progress') {
    return '⏳'
  }
  return '•'
}

const describeCountStatus = (
  status: LocationCountStatus,
  selectedUserId: string | null,
  selectedUserDisplayName: string | null,
) => {
  const baseLabel = `Comptage n°${status.countType}`
  if (status.status === 'completed') {
    const ownerSuffix = formatOwnerSuffix(status, selectedUserId, selectedUserDisplayName)
    return `${baseLabel} terminé${ownerSuffix ? ` ${ownerSuffix}` : ''}`
  }
  if (status.status === 'in_progress') {
    const ownerInfo = resolveStatusOwnerInfo(status, selectedUserId, selectedUserDisplayName)
    const ownerLabel = ownerInfo.label ? `par ${ownerInfo.label}` : null
    const duration = computeDurationLabel(status.startedAtUtc ?? null)
    const meta = [ownerLabel, duration].filter(Boolean).join(' • ')
    return `${baseLabel} en cours${meta ? ` (${meta})` : ''}`
  }
  return `${baseLabel} disponible`
}

export const InventoryCountTypeStep = () => {
  const navigate = useNavigate()
  const {
    selectedUser,
    location,
    sessionId,
    countType,
    setCountType,
    setSessionId,
    clearSession,
  } = useInventory()
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

  const displayName = location ? getLocationDisplayName(location.code, location.label) : ''
  const shouldDisplayLabel = location ? !isLocationLabelRedundant(location.code, location.label) : false

  const handleSelect = (type: CountType) => {
    const status = countStatuses.find((item) => item.countType === type)
    if (!status || zoneCompleted) {
      return
    }

    if (status.status === 'completed') {
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
      <Card className="flex flex-col gap-5">
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
        {location && (
          <div className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 dark:border-slate-700 dark:bg-slate-900/40">
            <div className="flex flex-col gap-1">
              <span className="text-xs font-semibold uppercase tracking-wide text-brand-500">
                Zone {location.code}
              </span>
              {shouldDisplayLabel && (
                <span className="text-sm font-medium text-slate-700 dark:text-slate-200">{location.label}</span>
              )}
            </div>
            <div className="mt-2 flex flex-col gap-1">
              {countStatuses
                .filter((status) => status.status !== 'not_started')
                .map((status) => (
                <span
                  key={`${location.id}-${status.countType}`}
                  className={`flex items-center gap-2 ${statusTextClass(status)}`}
                >
                  <span aria-hidden>{statusIcon(status)}</span>
                  <span>{describeCountStatus(status, selectedUserId, selectedUserDisplayName)}</span>
                </span>
              ))}
            </div>
          </div>
        )}
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          {DISPLAYED_COUNT_TYPES.map((option) => {
            const status = countStatuses.find((item) => item.countType === option)
            const ownerInfo = status
              ? resolveStatusOwnerInfo(status, selectedUserId, selectedUserDisplayName)
              : { label: null, isCurrentUser: false }
            const isSelected = countType === option
            const isCompleted = status?.status === 'completed'
            const isInProgress = status?.status === 'in_progress'
            const hasKnownOwner = Boolean(status?.ownerDisplayName || status?.ownerUserId)
            const isInProgressByUser = Boolean(isInProgress && ownerInfo.isCurrentUser)
            const isInProgressByOther = Boolean(isInProgress && !ownerInfo.isCurrentUser && hasKnownOwner)
            const restrictedCountType = option === CountType.Count1 || option === CountType.Count2
            const userCompletedOtherCount = restrictedCountType
              ? countStatuses.some(
                  (candidate) =>
                    candidate.countType !== option &&
                    DISPLAYED_COUNT_TYPES.includes(candidate.countType as CountType) &&
                    candidate.status === 'completed' &&
                    isStatusOwnedByCurrentUser(candidate, selectedUserId, selectedUserDisplayName),
                )
              : false
            const isDisabled = zoneCompleted || isCompleted || isInProgressByOther || userCompletedOtherCount
            const ownerSuffix = status
              ? formatOwnerSuffix(status, selectedUserId, selectedUserDisplayName, { fallback: 'un opérateur' })
              : null
            const helperMessage = (() => {
              if (zoneCompleted || isCompleted) {
                return `Comptage terminé${ownerSuffix ? ` ${ownerSuffix}` : ''}.`
              }
              if (isInProgressByOther) {
                return `En cours par ${ownerInfo.label ?? 'un autre utilisateur'}.`
              }
              if (isInProgressByUser) {
                return 'Reprenez votre comptage en cours.'
              }
              if (userCompletedOtherCount) {
                return 'Vous avez déjà réalisé un comptage pour cette zone.'
              }
              return 'Disponible.'
            })()
            return (
              <button
                key={option}
                type="button"
                onClick={() => handleSelect(option)}
                className={`flex flex-col gap-2 rounded-3xl border px-6 py-6 text-left transition-all ${
                  isDisabled
                    ? 'cursor-not-allowed border-slate-200 bg-slate-100 text-slate-400 dark:border-slate-700 dark:bg-slate-900/60 dark:text-slate-500'
                    : isSelected
                    ? 'border-brand-400 bg-brand-100 text-brand-700 dark:bg-brand-500/20 dark:text-brand-100'
                    : 'border-slate-200 bg-white text-slate-800 hover:border-brand-400/40 hover:bg-brand-50 dark:border-slate-700 dark:bg-slate-900/40 dark:text-slate-200'
                }`}
                data-testid={option === CountType.Count1 ? 'btn-count-type-1' : 'btn-count-type-2'}
                disabled={isDisabled}
                aria-disabled={isDisabled}
                title={isDisabled ? helperMessage : undefined}
              >
                <span className="text-4xl font-bold">Comptage n°{option}</span>
                <span className="text-sm text-slate-500 dark:text-slate-400">
                  {option === CountType.Count1
                    ? 'Premier passage pour initialiser la zone.'
                    : 'Second passage pour fiabiliser la zone.'}
                </span>
                <span className="text-xs font-medium text-slate-500 dark:text-slate-400">{helperMessage}</span>
              </button>
            )
          })}
        </div>
      </Card>
      <Button fullWidth className="py-4" variant="ghost" onClick={() => navigate('/inventory/location')}>
        Revenir aux zones
      </Button>
    </div>
  )
}
