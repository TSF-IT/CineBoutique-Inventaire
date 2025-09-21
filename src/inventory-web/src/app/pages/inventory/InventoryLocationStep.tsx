import { useCallback, useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { fetchLocations, restartInventoryRun } from '../../api/inventoryApi'
import { Button } from '../../components/Button'
import { Card } from '../../components/Card'
import { EmptyState } from '../../components/EmptyState'
import { LoadingIndicator } from '../../components/LoadingIndicator'
import { SlidingPanel } from '../../components/SlidingPanel'
import { TextField } from '../../components/TextField'
import { useInventory } from '../../contexts/InventoryContext'
import type { Location } from '../../types/inventory'
import { ApiError } from '../../api/client'

export const InventoryLocationStep = () => {
  const navigate = useNavigate()
  const { selectedUser, countType, location, setLocation, setSessionId, clearSession } = useInventory()
  const [search, setSearch] = useState('')
  const [locations, setLocations] = useState<Location[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [actionLocation, setActionLocation] = useState<Location | null>(null)
  const [actionSheetOpen, setActionSheetOpen] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [actionLoading, setActionLoading] = useState(false)

  const loadLocations = useCallback(
    async (options?: { isCancelled?: () => boolean }) => {
      if (!countType) {
        return
      }
      const isCancelled = options?.isCancelled ?? (() => false)
      setLoading(true)
      setError(null)
      try {
        const data = await fetchLocations({ countType })
        if (!isCancelled()) {
          setLocations(Array.isArray(data) ? data : [])
        }
      } catch (err) {
        if (!isCancelled()) {
          setLocations([])
          if (err instanceof ApiError) {
            setError(err.message)
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
    } else if (!countType) {
      navigate('/inventory/count-type', { replace: true })
    }
  }, [countType, navigate, selectedUser])

  useEffect(() => {
    if (!countType) {
      return
    }
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

  const handleStart = (zone: Location) => {
    setLocation(zone)
    clearSession()
    setSessionId(null)
    closeSheet()
    navigate('/inventory/session')
  }

  const handleJoin = (zone: Location) => {
    if (!zone.runId) {
      return
    }
    setLocation(zone)
    setSessionId(zone.runId)
    closeSheet()
    navigate('/inventory/session')
  }

  const handleRestart = async (zone: Location) => {
    if (!countType) {
      return
    }
    setActionLoading(true)
    setActionError(null)
    try {
      await restartInventoryRun(zone.id, countType)
      handleStart(zone)
    } catch (err) {
      if (err instanceof ApiError && err.message) {
        setActionError(err.message)
      } else {
        setActionError("Impossible de redémarrer cette zone pour le moment. Vérifiez votre connexion et réessayez.")
      }
    } finally {
      setActionLoading(false)
    }
  }

  const handleLocationSelection = (zone: Location) => {
    if (zone.isBusy) {
      setActionLocation(zone)
      setActionError(null)
      setActionSheetOpen(true)
    } else {
      handleStart(zone)
    }
  }

  const renderStatus = (zone: Location) => {
    if (zone.isBusy) {
      const durationLabel = computeDurationLabel(zone.startedAtUtc)
      return (
        <div className="flex flex-col gap-1" aria-live="polite">
          <span className="inline-flex min-h-[28px] items-center gap-2 rounded-full bg-amber-100 px-3 py-1 text-xs font-semibold uppercase tracking-wide text-amber-700 dark:bg-amber-500/20 dark:text-amber-200">
            <span aria-hidden>🔒</span>
            <span>Occupée</span>
          </span>
          <span className="text-sm text-amber-700 dark:text-amber-200">
            par {zone.inProgressBy ?? 'collaborateur inconnu'}
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
        <TextField
          label="Rechercher"
          placeholder="Ex. Réserve, Salle 2…"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
        />
        {loading && <LoadingIndicator label="Chargement des zones" />}
        {Boolean(error) && !loading && (
          <EmptyState
            title="Impossible de charger les zones"
            description={error ?? 'Vérifiez votre connexion ou réessayez plus tard.'}
            actionLabel="Réessayer"
            onAction={handleRetry}
          />
        )}
        {!loading && !error && (
          <div className="flex flex-col gap-3">
            {(Array.isArray(filteredLocations) ? filteredLocations : []).map((zone) => {
              const isSelected = location?.id === zone.id || actionLocation?.id === zone.id
              return (
                <button
                  key={zone.id}
                  type="button"
                  onClick={() => handleLocationSelection(zone)}
                  className={`flex min-h-[68px] flex-col gap-3 rounded-3xl border px-5 py-4 text-left transition-all focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-500 ${
                    isSelected
                      ? 'border-brand-400 bg-brand-500/10 text-brand-700 dark:bg-brand-500/20 dark:text-brand-100'
                      : 'border-slate-200 bg-white text-slate-800 hover:border-brand-400/40 hover:bg-brand-50 dark:border-slate-700 dark:bg-slate-900/40 dark:text-slate-200'
                  }`}
                  aria-label={
                    zone.isBusy
                      ? `Zone ${zone.label} occupée, afficher les options`
                      : `Zone ${zone.label} libre, démarrer le comptage`
                  }
                >
                  <div className="flex items-start justify-between gap-3">
                    <div className="flex flex-col gap-1">
                      <span className="text-sm font-semibold uppercase tracking-wider text-brand-500">
                        {zone.code}
                      </span>
                      <span className="text-lg font-semibold">{zone.label}</span>
                      {zone.description && (
                        <span className="text-sm text-slate-500 dark:text-slate-400">{zone.description}</span>
                      )}
                    </div>
                    {renderStatus(zone)}
                  </div>
                  <span className="mt-2 inline-flex items-center justify-between text-sm font-medium text-brand-600 dark:text-brand-200">
                    {zone.isBusy ? 'Voir les options' : 'Démarrer'}
                    <span aria-hidden>›</span>
                  </span>
                </button>
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
        title={actionLocation?.label ? `Actions pour ${actionLocation.label}` : 'Actions de zone'}
      >
        {actionLocation?.isBusy ? (
          <div className="flex flex-col gap-4">
            <p className="text-sm text-slate-600 dark:text-slate-300">
              Un comptage est en cours pour cette zone. Sélectionnez l&apos;action souhaitée.
            </p>
            <Button
              fullWidth
              className="py-3"
              onClick={() => actionLocation && handleJoin(actionLocation)}
              data-testid="join-run"
            >
              Rejoindre le comptage en cours
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
          </div>
        ) : (
          <div className="flex flex-col gap-4">
            <p className="text-sm text-slate-600 dark:text-slate-300">
              Cette zone est disponible. Lancez le comptage quand vous êtes prêt.
            </p>
            <Button fullWidth className="py-3" onClick={() => actionLocation && handleStart(actionLocation)}>
              Démarrer le comptage
            </Button>
          </div>
        )}
      </SlidingPanel>
    </div>
  )
}
