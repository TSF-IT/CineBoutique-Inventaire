import { useCallback, useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { fetchInventorySummary, fetchLocations } from '../../api/inventoryApi'
import { Button } from '../../components/ui/Button'
import { Input } from '../../components/ui/Input'
import { Card } from '../../components/Card'
import { EmptyState } from '../../components/EmptyState'
import { ErrorPanel } from '../../components/ErrorPanel'
import { LoadingIndicator } from '../../components/LoadingIndicator'
import { useInventory } from '../../contexts/InventoryContext'
import { CountType } from '../../types/inventory'
import type { Location, LocationCountStatus } from '../../types/inventory'
import { getLocationDisplayName, isLocationLabelRedundant } from '../../utils/locationDisplay'
import type { HttpError } from '@/lib/api/http'

const DEV_API_UNREACHABLE_HINT =
  "Impossible de joindre l’API : vérifie que le backend tourne (curl http://localhost:8080/healthz) ou que le proxy Vite est actif."

const isHttpError = (value: unknown): value is HttpError =>
  typeof value === 'object' &&
  value !== null &&
  typeof (value as { status?: unknown }).status === 'number' &&
  typeof (value as { url?: unknown }).url === 'string'

const extractHttpDetail = (error: HttpError): string | undefined =>
  (error.problem as { detail?: string; title?: string } | undefined)?.detail ||
  (error.problem as { title?: string } | undefined)?.title ||
  error.body ||
  undefined

const resolveErrorPanel = (
  error: HttpError | Error | string | null,
): { title: string; details: string } | null => {
  if (!error) {
    return null
  }
  if (isHttpError(error)) {
    const detail = extractHttpDetail(error)
    const problem = error.problem as { contentType?: string; snippet?: string } | undefined
    const snippet = problem?.snippet
    const looksLikeHtmlSnippet = typeof snippet === 'string' && /<(!doctype\s+html|html)/i.test(snippet.trimStart())
    if (import.meta.env.DEV && error.status === 0 && looksLikeHtmlSnippet) {
      const diagnostics = [
        'La réponse n’est pas du JSON (probable proxy Vite non relié à l’API).',
      ]
      if (error.url) {
        diagnostics.push(`URL: ${error.url}`)
      }
      if (problem?.contentType) {
        diagnostics.push(`Content-Type: ${problem.contentType}`)
      }
      diagnostics.push('', 'Checklist dev :', '- L’API tourne-t-elle sur http://localhost:8080 ?', '- Le proxy Vite est-il actif ?')
      return {
        title: 'Erreur API',
        details: diagnostics.filter(Boolean).join('\n'),
      }
    }
    if (import.meta.env.DEV && error.status === 404) {
      const diagnostics = [DEV_API_UNREACHABLE_HINT]
      if (error.url) {
        diagnostics.push(`URL: ${error.url}`)
      }
      if (detail) {
        diagnostics.push(detail)
      }
      return { title: 'Erreur API', details: diagnostics.join('\n') }
    }
    return {
      title: 'Erreur API',
      details: detail ?? (typeof error.status === 'number' ? `HTTP ${error.status}` : 'Impossible de joindre l’API.'),
    }
  }
  if (error instanceof Error) {
    return { title: 'Erreur', details: error.message }
  }
  if (typeof error === 'string') {
    return { title: 'Erreur', details: error }
  }
  return { title: 'Erreur', details: 'Une erreur inattendue est survenue.' }
}

const DISPLAYED_COUNT_TYPES: CountType[] = [CountType.Count1, CountType.Count2]

export const InventoryLocationStep = () => {
  const navigate = useNavigate()
  const { selectedUser, location, sessionId, setLocation, setSessionId, clearSession, setCountType } = useInventory()
  const [search, setSearch] = useState('')
  const [locations, setLocations] = useState<Location[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<HttpError | Error | string | null>(null)
  const [conflictLookup, setConflictLookup] = useState<Map<string, true>>(new Map())
  const [conflictsLoaded, setConflictsLoaded] = useState(false)
  const loadLocations = useCallback(
    async (options?: { isCancelled?: () => boolean }) => {
      const isCancelled = options?.isCancelled ?? (() => false)
      setLoading(true)
      setError(null)
      try {
        const data = await fetchLocations()
        if (!isCancelled()) {
          setLocations(Array.isArray(data) ? data : [])
        }
      } catch (err) {
        if (!isCancelled()) {
          setLocations([])
          if (isHttpError(err)) {
            setError(err)
          } else if (err instanceof Error) {
            setError(err)
          } else {
            setError('Impossible de charger les zones pour le moment.')
          }
        }
      } finally {
        if (!isCancelled()) {
          setLoading(false)
        }
      }
    },
    [],
  )

  const loadConflicts = useCallback(
    async (options?: { isCancelled?: () => boolean }) => {
      const isCancelled = options?.isCancelled ?? (() => false)
      try {
        const summary = await fetchInventorySummary()
        if (isCancelled()) {
          return
        }

        const nextLookup = new Map<string, true>()
        for (const conflict of summary.conflictDetails ?? []) {
          const idKey = conflict.locationId?.trim().toLowerCase()
          if (idKey) {
            nextLookup.set(idKey, true)
          }
          const codeKey = conflict.locationCode?.trim().toLowerCase()
          if (codeKey) {
            nextLookup.set(codeKey, true)
          }
        }

        setConflictLookup(nextLookup)
        setConflictsLoaded(true)
      } catch (err) {
        if (isCancelled()) {
          return
        }
        if (import.meta.env.DEV) {
          console.error('Impossible de charger les informations de conflit.', err)
        }
        setConflictLookup(new Map())
        setConflictsLoaded(false)
      }
    },
    [],
  )

  useEffect(() => {
    if (!selectedUser) {
      navigate('/inventory/start', { replace: true })
    }
  }, [navigate, selectedUser])

  useEffect(() => {
    let cancelled = false
    void loadLocations({ isCancelled: () => cancelled })
    void loadConflicts({ isCancelled: () => cancelled })
    return () => {
      cancelled = true
    }
  }, [loadConflicts, loadLocations])

  const filteredLocations = useMemo(() => {
    const safeList = Array.isArray(locations) ? locations : []
    if (!search.trim()) {
      return safeList
    }
    const lowerSearch = search.toLowerCase()
    return safeList.filter((zone) =>
      zone.label.toLowerCase().includes(lowerSearch) || zone.code.toLowerCase().includes(lowerSearch),
    )
  }, [locations, search])

  const handleRetry = () => {
    void loadLocations()
    void loadConflicts()
  }

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
      return 'depuis moins d\'1 min'
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

  const getRelevantStatuses = (zone: Location): LocationCountStatus[] => {
    if (!Array.isArray(zone.countStatuses)) {
      return []
    }
    return [...zone.countStatuses]
      .filter((status): status is LocationCountStatus =>
        DISPLAYED_COUNT_TYPES.includes(status.countType as CountType),
      )
      .sort((a, b) => a.countType - b.countType)
  }

  const getVisibleStatuses = (zone: Location): LocationCountStatus[] =>
    getRelevantStatuses(zone).filter((status) => status.status !== 'not_started')

  const isZoneCompleted = (zone: Location) => {
    const statuses = getRelevantStatuses(zone)
    return statuses.length > 0 && statuses.every((status) => status.status === 'completed')
  }

  const describeCountStatus = (status: LocationCountStatus) => {
    const baseLabel = `Comptage n°${status.countType}`
    if (status.status === 'completed') {
      return `${baseLabel} terminé`
    }
    if (status.status === 'in_progress') {
      const ownerLabel =
        status.operatorDisplayName && status.operatorDisplayName === selectedUser
          ? 'par vous'
          : status.operatorDisplayName
          ? `par ${status.operatorDisplayName}`
          : null
      const duration = computeDurationLabel(status.startedAtUtc ?? null)
      const meta = [ownerLabel, duration].filter(Boolean).join(' • ')
      return `${baseLabel} en cours${meta ? ` (${meta})` : ''}`
    }
    return baseLabel
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

  const handleLocationSelection = (zone: Location) => {
    if (isZoneCompleted(zone)) {
      return
    }

    const statuses = getRelevantStatuses(zone)
    const activeStatus = statuses.find((status) => status.status === 'in_progress')
    const nextSessionId = activeStatus?.runId ?? zone.activeRunId ?? null
    const isSameLocation = location?.id === zone.id
    const isSameSession = isSameLocation && sessionId === nextSessionId

    if (!isSameSession) {
      clearSession()
    }

    setSessionId(nextSessionId ?? null)
    setCountType(null)
    setLocation(zone)
    navigate('/inventory/count-type')
  }

  const errorPanel = useMemo(() => resolveErrorPanel(error), [error])

  return (
    <div className="flex flex-col gap-6" data-testid="page-location">
      <Card className="flex flex-col gap-4">
        <div className="flex items-center justify-between gap-3">
          <div>
            <h2 className="text-2xl font-semibold text-slate-900 dark:text-white">Sélectionnez la zone</h2>
            <p className="text-sm text-slate-500 dark:text-slate-400">
              Choisissez la zone à inventorier.
            </p>
          </div>
          <Button variant="ghost" onClick={handleRetry} disabled={loading}>
            Actualiser
          </Button>
        </div>
        <Input
          label="Rechercher"
          name="locationQuery"
          placeholder="Ex. Réserve, Salle 2…"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
        />
        {loading && <LoadingIndicator label="Chargement des zones" />}
        {!loading && errorPanel && (
          <ErrorPanel
            title={errorPanel.title}
            details={errorPanel.details}
            actionLabel="Réessayer"
            onAction={handleRetry}
          />
        )}
        {!loading && !errorPanel && (
          <div className="flex flex-col gap-3">
            {(Array.isArray(filteredLocations) ? filteredLocations : []).map((zone) => {
              const visibleStatuses = getVisibleStatuses(zone)
              const zoneCompleted = isZoneCompleted(zone)
              const isSelected = location?.id === zone.id
              const statusSummary =
                visibleStatuses.length > 0
                  ? visibleStatuses.map((status) => describeCountStatus(status)).join(', ')
                  : zoneCompleted
                    ? 'Comptages terminés'
                    : 'Aucun comptage en cours'
              const toneClass = zoneCompleted
                ? 'border-slate-200 bg-slate-50 text-slate-500 opacity-80 dark:border-slate-700 dark:bg-slate-900/60 dark:text-slate-400'
                : isSelected
                  ? 'border-brand-400 bg-brand-500/10 text-brand-700 dark:bg-brand-500/20 dark:text-brand-100'
                  : 'border-slate-200 bg-white text-slate-800 dark:border-slate-700 dark:bg-slate-900/40 dark:text-slate-200'
              const displayName = getLocationDisplayName(zone.code, zone.label)
              const shouldDisplayLabel = !isLocationLabelRedundant(zone.code, zone.label)
              const conflictStatus = (() => {
                if (!conflictsLoaded) {
                  return null
                }
                const idKey = zone.id?.trim().toLowerCase()
                const codeKey = zone.code?.trim().toLowerCase()
                const hasConflict = Boolean(
                  (idKey && conflictLookup.get(idKey)) || (codeKey && conflictLookup.get(codeKey)),
                )
                return hasConflict ? 'conflict' : 'none'
              })()
              return (
                <div
                  key={zone.id}
                  className={`flex flex-col gap-3 rounded-3xl border px-5 py-4 transition-all ${toneClass}`}
                  data-testid={`zone-card-${zone.id}`}
                >
                  <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                    <div className="flex flex-col gap-1">
                      <span className="text-sm font-semibold uppercase tracking-wider text-brand-500">
                        {zone.code}
                      </span>
                      {shouldDisplayLabel && (
                        <span className="text-lg font-semibold">{zone.label}</span>
                      )}
                    </div>
                    {visibleStatuses.length > 0 && (
                      <div className="flex flex-col gap-1" aria-live="polite">
                        {visibleStatuses.map((status) => (
                          <span
                            key={`${zone.id}-${status.countType}`}
                            className={`flex items-center gap-2 ${statusTextClass(status)}`}
                          >
                            <span aria-hidden>{statusIcon(status)}</span>
                            <span>{describeCountStatus(status)}</span>
                          </span>
                        ))}
                      </div>
                    )}
                  </div>
                  {zoneCompleted && (
                    <div className="text-xs font-medium">
                      <p className="text-slate-500 dark:text-slate-400">
                        Les comptages 1 et 2 sont terminés pour cette zone.
                      </p>
                      {conflictStatus === 'none' && (
                        <p className="mt-1 flex items-center gap-2 text-emerald-600 dark:text-emerald-300">
                          <span aria-hidden>✅</span>
                          <span>Aucun conflit</span>
                        </p>
                      )}
                      {conflictStatus === 'conflict' && (
                        <p className="mt-1 flex items-center gap-2 text-rose-600 dark:text-rose-300">
                          <span aria-hidden>⚠️</span>
                          <span>Conflit détecté</span>
                        </p>
                      )}
                    </div>
                  )}
                  <div className="mt-2 flex flex-wrap items-center gap-2">
                    <Button
                      data-testid="btn-select-zone"
                      aria-label={
                        visibleStatuses.length > 0 || zoneCompleted
                          ? `Zone ${displayName} – ${statusSummary}`
                          : `Zone ${displayName}`
                      }
                      onClick={() => handleLocationSelection(zone)}
                      disabled={zoneCompleted}
                      aria-disabled={zoneCompleted}
                      title={zoneCompleted ? 'Les deux comptages sont terminés' : undefined}
                    >
                      {zoneCompleted ? 'Zone terminée' : 'Choisir cette zone'}
                    </Button>
                  </div>
                </div>
              )
            })}
            {(Array.isArray(filteredLocations) ? filteredLocations : []).length === 0 && (
              <EmptyState title="Aucune zone" description="Aucune zone ne correspond à votre recherche." />
            )}
          </div>
        )}
      </Card>
    </div>
  )
}
