import { useCallback, useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { fetchLocations, restartInventoryRun } from '../../api/inventoryApi'
import { Button } from '../../components/ui/Button'
import { Input } from '../../components/ui/Input'
import { Card } from '../../components/Card'
import { EmptyState } from '../../components/EmptyState'
import { ErrorPanel } from '../../components/ErrorPanel'
import { LoadingIndicator } from '../../components/LoadingIndicator'
import { SlidingPanel } from '../../components/SlidingPanel'
import { useInventory } from '../../contexts/InventoryContext'
import { CountType } from '../../types/inventory'
import type { Location } from '../../types/inventory'
import type { HttpError } from '@/lib/api/http'

const DEV_API_UNREACHABLE_HINT =
  "Impossible de joindre l’API : vérifie que le backend tourne (curl http://localhost:8080/healthz) ou que le proxy Vite est actif."

const isHttpError = (value: unknown): value is HttpError =>
  typeof value === 'object' &&
  value !== null &&
  typeof (value as { status?: unknown }).status === 'number' &&
  typeof (value as { url?: unknown }).url === 'string'

const truncate = (value: string, maxLength = 180) =>
  value.length <= maxLength ? value : `${value.slice(0, maxLength)}…`

const extractHttpDetail = (error: HttpError): string | undefined =>
  (error.problem as { detail?: string; title?: string } | undefined)?.detail ||
  (error.problem as { title?: string } | undefined)?.title ||
  error.body ||
  undefined

const formatHttpError = (error: HttpError, prefix = 'Erreur réseau') => {
  const detail = extractHttpDetail(error)
  if (import.meta.env.DEV && error.status === 404) {
    const diagnostics = [DEV_API_UNREACHABLE_HINT]
    if (error.url) {
      diagnostics.push(`URL: ${error.url}`)
    }
    if (detail) {
      diagnostics.push(`Détail: ${truncate(detail)}`)
    }
    return diagnostics.join(' | ')
  }

  const parts = [prefix]
  if (error.status) {
    parts.push(`HTTP ${error.status}`)
  }
  if (error.url) {
    parts.push(`URL: ${error.url}`)
  }
  if (detail) {
    parts.push(`Détail: ${truncate(detail)}`)
  }
  return parts.join(' | ')
}

const resolveErrorPanel = (
  error: HttpError | Error | string | null,
): { title: string; details: string } | null => {
  if (!error) {
    return null
  }
  if (isHttpError(error)) {
    const detail = extractHttpDetail(error)
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

const toCountType = (value: number | null | undefined): CountType | undefined => {
  if (value === CountType.Count1) {
    return CountType.Count1
  }
  if (value === CountType.Count2) {
    return CountType.Count2
  }
  if (value === CountType.Count3) {
    return CountType.Count3
  }
  return undefined
}

export const InventoryLocationStep = () => {
  const navigate = useNavigate()
  const { selectedUser, countType, location, setLocation, setSessionId, clearSession } = useInventory()
  const [search, setSearch] = useState('')
  const [locations, setLocations] = useState<Location[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<HttpError | Error | string | null>(null)
  const [actionLocation, setActionLocation] = useState<Location | null>(null)
  const [actionSheetOpen, setActionSheetOpen] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [actionLoading, setActionLoading] = useState(false)

  const loadLocations = useCallback(
    async (options?: { isCancelled?: () => boolean }) => {
      const isCancelled = options?.isCancelled ?? (() => false)
      setLoading(true)
      setError(null)
      try {
        const data = await fetchLocations(countType ? { countType } : undefined)
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
    [countType],
  )

  useEffect(() => {
    if (!selectedUser) {
      navigate('/inventory/start', { replace: true })
    }
  }, [navigate, selectedUser])

  useEffect(() => {
    let cancelled = false
    void loadLocations({ isCancelled: () => cancelled })
    return () => {
      cancelled = true
    }
  }, [countType, loadLocations])

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
  }

  const computeDurationLabel = (startedAtUtc: string | null | undefined) => {
    if (!startedAtUtc) {
      return null
    }
    const started = new Date(startedAtUtc)
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

  const closeSheet = () => {
    setActionSheetOpen(false)
    setActionLocation(null)
    setActionError(null)
    setActionLoading(false)
  }

  const proceedToCountTypeStep = (
    zone: Location,
    options?: { sessionId?: string | null; resetSession?: boolean },
  ) => {
    setLocation(zone)
    if (options?.resetSession) {
      clearSession()
    }
    if (options && 'sessionId' in options) {
      setSessionId(options.sessionId ?? null)
    }
    closeSheet()
    navigate('/inventory/count-type')
  }

  const handleJoin = (zone: Location) => {
    if (!zone.activeRunId) {
      return
    }
    proceedToCountTypeStep(zone, { sessionId: zone.activeRunId, resetSession: false })
  }

  const handleRestart = async (zone: Location) => {
    setActionLoading(true)
    setActionError(null)
    try {
      const fallbackCountType = toCountType(zone.activeCountType) ?? CountType.Count1
      const effectiveCountType = countType ?? fallbackCountType
      await restartInventoryRun(zone.id, effectiveCountType)
      proceedToCountTypeStep(zone, { sessionId: null, resetSession: true })
    } catch (err) {
      if (isHttpError(err)) {
        setActionError(formatHttpError(err, 'Redémarrage impossible'))
      } else if (err instanceof Error && err.message) {
        setActionError(err.message)
      } else {
        setActionError("Impossible de redémarrer cette zone pour le moment. Vérifiez votre connexion et réessayez.")
      }
    } finally {
      setActionLoading(false)
    }
  }

  const handleSessionOptions = (zone: Location) => {
    setActionLocation(zone)
    setActionError(null)
    setActionSheetOpen(true)
  }

  const handleLocationSelection = (zone: Location) => {
    proceedToCountTypeStep(zone, {
      sessionId: zone.activeRunId ?? null,
      resetSession: !zone.activeRunId,
    })
  }

  const renderStatus = (zone: Location) => {
    if (zone.isBusy) {
      const durationLabel = computeDurationLabel(zone.activeStartedAtUtc ?? null)
      return (
        <div className="flex flex-col gap-1" aria-live="polite">
          <span className="inline-flex min-h-[28px] items-center gap-2 rounded-full bg-red-100 px-3 py-1 text-xs font-semibold uppercase tracking-wide text-red-700 dark:bg-red-500/20 dark:text-red-200">
            <span aria-hidden>🔒</span>
            <span>Occupée</span>
          </span>
          <span className="text-sm text-red-700 dark:text-red-200">
            par {zone.busyBy ?? 'collaborateur inconnu'}
            {durationLabel ? ` • ${durationLabel}` : ''}
          </span>
        </div>
      )
    }
    return (
      <span className="inline-flex min-h-[28px] items-center gap-2 rounded-full bg-emerald-100 px-3 py-1 text-xs font-semibold uppercase tracking-wide text-emerald-700 dark:bg-emerald-500/20 dark:text-emerald-100">
        <span aria-hidden>✅</span>
        <span>Libre</span>
      </span>
    )
  }

  const errorPanel = useMemo(() => resolveErrorPanel(error), [error])

  return (
    <div className="flex flex-col gap-6">
      <Card className="flex flex-col gap-4">
        <div className="flex items-center justify-between gap-3">
          <div>
            <h2 className="text-2xl font-semibold text-slate-900 dark:text-white">Sélectionnez la zone</h2>
            <p className="text-sm text-slate-500 dark:text-slate-400">
              Choisissez la zone physique ou logique à inventorier.
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
              const isSelected = location?.id === zone.id
              return (
                <div
                  key={zone.id}
                  className={`flex flex-col gap-3 rounded-3xl border px-5 py-4 transition-all ${
                    isSelected
                      ? 'border-brand-400 bg-brand-500/10 text-brand-700 dark:bg-brand-500/20 dark:text-brand-100'
                      : 'border-slate-200 bg-white text-slate-800 dark:border-slate-700 dark:bg-slate-900/40 dark:text-slate-200'
                  }`}
                >
                  <div className="flex items-start justify-between gap-3">
                    <div className="flex flex-col gap-1">
                      <span className="text-sm font-semibold uppercase tracking-wider text-brand-500">
                        {zone.code}
                      </span>
                      <span className="text-lg font-semibold">{zone.label}</span>
                    </div>
                    {renderStatus(zone)}
                  </div>
                  <div className="mt-2 flex flex-wrap items-center gap-2">
                    <Button onClick={() => handleLocationSelection(zone)}>
                      Choisir cette zone
                    </Button>
                    {zone.isBusy && (
                      <Button variant="ghost" onClick={() => handleSessionOptions(zone)}>
                        Gérer la session en cours
                      </Button>
                    )}
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
      <SlidingPanel
        open={actionSheetOpen}
        onClose={closeSheet}
        title={actionLocation?.label ? `Zone ${actionLocation.label}` : 'Actions de zone'}
      >
        {actionLocation?.isBusy ? (
          <div className="flex flex-col gap-4">
            <p className="text-sm text-slate-600 dark:text-slate-300">
              Un comptage est en cours pour cette zone. Sélectionnez l&apos;action souhaitée.
            </p>
            <Button
              fullWidth
              className="py-3"
              variant="secondary"
              disabled={!actionLocation?.activeRunId}
              onClick={() => actionLocation && handleJoin(actionLocation)}
              data-testid="join-run"
            >
              Reprendre le comptage en cours
            </Button>
            <Button
              fullWidth
              variant="ghost"
              className="py-3"
              disabled={actionLoading}
              aria-disabled={actionLoading}
              onClick={() => actionLocation && void handleRestart(actionLocation)}
            >
              {actionLoading ? 'Redémarrage…' : 'Redémarrer un nouveau comptage'}
            </Button>
            {actionError && (
              <p className="text-sm text-red-600 dark:text-red-300" role="alert">
                {actionError}
              </p>
            )}
            {!actionLocation?.activeRunId && (
              <p className="text-xs text-slate-500 dark:text-slate-400">
                Aucun run actif détecté. Lancez un nouveau comptage si nécessaire.
              </p>
            )}
            <Button fullWidth variant="ghost" className="py-3" onClick={closeSheet}>
              Annuler
            </Button>
          </div>
        ) : (
          <div className="flex flex-col gap-4">
            <p className="text-sm text-slate-600 dark:text-slate-300">
              Cette zone est disponible. Préparez la prochaine étape lorsque vous êtes prêt.
            </p>
            <Button
              fullWidth
              className="py-3"
              onClick={() => actionLocation && proceedToCountTypeStep(actionLocation, { sessionId: null, resetSession: true })}
            >
              Choisir cette zone
            </Button>
          </div>
        )}
      </SlidingPanel>
    </div>
  )
}
