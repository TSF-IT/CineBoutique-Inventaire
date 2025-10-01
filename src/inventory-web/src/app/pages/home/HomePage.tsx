// Modifications : chargement des zones pour le compteur terminé et panneau enrichi des runs ouverts.
import { useCallback, useMemo, useState } from 'react'
import clsx from 'clsx'
import { Link, useNavigate } from 'react-router-dom'
import { fetchInventorySummary, fetchLocations } from '../../api/inventoryApi'
import { Button } from '../../components/ui/Button'
import { Card } from '../../components/Card'
import { ErrorPanel } from '../../components/ErrorPanel'
import { LoadingIndicator } from '../../components/LoadingIndicator'
import { Page } from '../../components/Page'
import { SectionTitle } from '../../components/SectionTitle'
import { ConflictZoneModal } from '../../components/Conflicts/ConflictZoneModal'
import { RunsOverviewModal } from '../../components/Runs/RunsOverviewModal'
import { useAsync } from '../../hooks/useAsync'
import type { ConflictZoneSummary, InventorySummary, Location } from '../../types/inventory'
import type { HttpError } from '@/lib/api/http'

const isHttpError = (value: unknown): value is HttpError =>
  typeof value === 'object' &&
  value !== null &&
  typeof (value as { status?: unknown }).status === 'number' &&
  typeof (value as { url?: unknown }).url === 'string'

const formatLastActivity = (value: string | null) => {
  if (!value) {
    return 'Non disponible'
  }
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return 'Non disponible'
  }
  return new Intl.DateTimeFormat('fr-FR', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date)
}

const describeError = (error: unknown): { title: string; details?: string } | null => {
  if (!error) {
    return null
  }
  if (isHttpError(error)) {
    const detail =
      (error.problem as { detail?: string; title?: string } | undefined)?.detail ||
      (error.problem as { title?: string } | undefined)?.title ||
      error.body ||
      (typeof error.status === 'number' ? `HTTP ${error.status}` : undefined)
    const enrichedDetail =
      import.meta.env.DEV && error.status === 404 && detail
        ? `${detail}\nVérifie que l’API répond sur ${error.url ?? 'http://localhost:8080/api'}.`
        : detail
    return {
      title: 'Erreur API',
      details: enrichedDetail ?? 'Impossible de joindre le backend.',
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

export const HomePage = () => {
  const navigate = useNavigate()
  const [runsModalOpen, setRunsModalOpen] = useState(false)
  const [conflictModalOpen, setConflictModalOpen] = useState(false)
  const [selectedZone, setSelectedZone] = useState<ConflictZoneSummary | null>(null)
  const onError = useCallback((e: unknown) => {
    const err = e as HttpError
    console.error('[home] http error', err)
  }, [])

  const {
    data: summaryData,
    loading: summaryLoading,
    error: summaryError,
    execute: executeSummary,
  } = useAsync(fetchInventorySummary, [], {
    initialValue: null,
    onError,
  })

  const {
    data: locationsData,
    loading: locationsLoading,
    error: locationsError,
    execute: executeLocations,
  } = useAsync<Location[]>(fetchLocations, [], {
    initialValue: [],
    onError,
  })

  const handleRetry = useCallback(() => {
    void executeSummary()
    void executeLocations()
  }, [executeSummary, executeLocations])

  const combinedError = summaryError ?? locationsError
  const combinedLoading = summaryLoading || locationsLoading

  const errorDetails = useMemo(() => describeError(combinedError), [combinedError])

  const displaySummary: InventorySummary | null = summaryData ?? null
  const openRunsCount = displaySummary?.openRuns ?? 0
  const conflictCount = displaySummary?.conflicts ?? 0
  const openRunDetails = displaySummary?.openRunDetails ?? []
  const completedRunDetails = displaySummary?.completedRunDetails ?? []
  const conflictZones = useMemo(() => displaySummary?.conflictZones ?? [], [displaySummary])
  const locations = locationsData ?? []
  const completedRuns = useMemo(() => {
    return locations.reduce((acc, location) => {
      const statuses = location.countStatuses ?? []
      const completedTypes = statuses.reduce<Set<number>>((set, status) => {
        if (status.status === 'completed' && (status.countType === 1 || status.countType === 2)) {
          set.add(status.countType)
        }
        return set
      }, new Set<number>())
      return acc + completedTypes.size
    }, 0)
  }, [locations])

  const totalExpected = useMemo(() => locations.length * 2, [locations.length])
  const hasOpenRuns = openRunsCount > 0
  const hasConflicts = conflictCount > 0
  const canOpenRunsModal = openRunDetails.length > 0 || completedRunDetails.length > 0
  const canOpenConflicts = hasConflicts && conflictZones.length > 0

  const handleOpenRunsClick = useCallback(() => {
    if (canOpenRunsModal) {
      setRunsModalOpen(true)
    }
  }, [canOpenRunsModal])

  const openConflictModal = useCallback((zone: ConflictZoneSummary) => {
    setSelectedZone(zone)
    setConflictModalOpen(true)
  }, [])

  const handleConflictModalClose = useCallback(() => {
    setConflictModalOpen(false)
    setSelectedZone(null)
  }, [])

  return (
    <Page>
      <header className="flex flex-col gap-4">
        <p className="text-sm uppercase tracking-[0.3em] text-brand-600 dark:text-brand-200">CinéBoutique</p>
        <h1 className="text-4xl font-black leading-tight text-slate-900 dark:text-white sm:text-5xl">
          Inventaire simplifié
        </h1>
        <p className="max-w-xl text-base text-slate-600 dark:text-slate-300">
          Lancez un comptage en quelques gestes, scannez les produits depuis la caméra ou une douchette Bluetooth et
          assurez un suivi fiable de vos zones.
        </p>
      </header>

      <Card className="flex flex-col gap-4">
        <SectionTitle>État de l’inventaire</SectionTitle>
        {combinedLoading && <LoadingIndicator label="Chargement des indicateurs" />}
        {!combinedLoading && errorDetails && (
          <ErrorPanel title={errorDetails.title} details={errorDetails.details} actionLabel="Réessayer" onAction={handleRetry} />
        )}
        {!combinedLoading && !errorDetails && displaySummary && (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <button
              type="button"
              onClick={handleOpenRunsClick}
              disabled={!canOpenRunsModal}
              className={clsx(
                'flex flex-col rounded-2xl border border-brand-300 bg-brand-100/70 p-5 text-left transition dark:border-brand-500/30 dark:bg-brand-500/10',
                canOpenRunsModal
                  ? 'cursor-pointer focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500 focus-visible:ring-offset-2'
                  : 'cursor-default'
              )}
            >
              <p className="text-sm uppercase text-brand-600 dark:text-brand-200">Comptages en cours</p>
              <p
                className={clsx(
                  'mt-2 font-semibold',
                  hasOpenRuns
                    ? 'text-4xl text-brand-700 dark:text-white'
                    : 'text-lg text-brand-700 dark:text-brand-100'
                )}
              >
                {hasOpenRuns ? openRunsCount : 'Aucun comptage en cours'}
              </p>
              {canOpenRunsModal && (
                <p className="mt-1 text-xs text-brand-700/80 dark:text-brand-200/80">Touchez pour voir le détail</p>
              )}
              <p className="mt-3 text-xs text-brand-700/80 dark:text-brand-100/70">
                Comptages terminés : {completedRuns} / {totalExpected || 0}
              </p>
            </button>
            <div
              className={clsx(
                'flex flex-col rounded-2xl border p-5 text-left transition',
                hasConflicts
                  ? 'border-rose-300 bg-rose-100/70 dark:border-rose-500/40 dark:bg-rose-500/10'
                  : 'border-emerald-300 bg-emerald-100/70 dark:border-emerald-500/30 dark:bg-emerald-500/10'
              )}
            >
              <p
                className={clsx(
                  'text-sm uppercase',
                  hasConflicts ? 'text-rose-700 dark:text-rose-200' : 'text-emerald-700 dark:text-emerald-200'
                )}
              >
                Conflits
              </p>
              <p
                className={clsx(
                  'mt-2 font-semibold',
                  hasConflicts
                    ? 'text-4xl text-rose-700 dark:text-rose-100'
                    : 'text-lg text-emerald-800 dark:text-emerald-100'
                )}
              >
                {hasConflicts ? conflictCount : 'Aucun conflit'}
              </p>
              {canOpenConflicts && (
                <p className="mt-1 text-xs text-rose-700/80 dark:text-rose-200/70">
                  Touchez une zone pour voir le détail
                </p>
              )}
              <div className="mt-4 flex flex-col gap-2">
                {hasConflicts && conflictZones.length > 0 ? (
                  <ul className="divide-y divide-rose-200/70 rounded-2xl border border-rose-200/70 bg-white/60 dark:divide-rose-500/40 dark:border-rose-500/30 dark:bg-rose-500/10">
                    {conflictZones.map((zone) => (
                      <li key={zone.locationId}>
                        <button
                          type="button"
                          onClick={() => openConflictModal(zone)}
                          className="flex w-full items-center justify-between gap-3 px-4 py-3 text-left text-sm transition hover:bg-rose-100/70 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-rose-500 focus-visible:ring-offset-2 dark:hover:bg-rose-500/20"
                        >
                          <span className="font-medium text-rose-900 dark:text-rose-100">
                            {zone.locationCode} · {zone.locationLabel}
                          </span>
                          <span className="text-xs font-semibold uppercase tracking-wide text-rose-700 dark:text-rose-200">
                            {zone.conflictLines} réf.
                          </span>
                        </button>
                      </li>
                    ))}
                  </ul>
                ) : (
                  <p className="rounded-2xl border border-emerald-200 bg-white/60 px-4 py-3 text-xs text-emerald-700 dark:border-emerald-500/40 dark:bg-emerald-500/10 dark:text-emerald-200">
                    Aucune divergence détectée.
                  </p>
                )}
              </div>
            </div>
            <div className="rounded-2xl border border-slate-300 bg-slate-100/70 p-5 dark:border-slate-600/60 dark:bg-slate-900/40">
              <p className="text-sm uppercase text-slate-600 dark:text-slate-300">Dernière activité</p>
              <p className="mt-2 text-lg font-semibold text-slate-800 dark:text-white">
                {formatLastActivity(displaySummary.lastActivityUtc)}
              </p>
            </div>
          </div>
        )}
        {!combinedLoading && !errorDetails && !displaySummary && (
          <p className="text-sm text-slate-500 dark:text-slate-400">
            Les indicateurs ne sont pas disponibles pour le moment.
          </p>
        )}
      </Card>

      <div className="flex flex-col gap-4">
        <Button
          fullWidth
          className="py-5 text-lg"
          onClick={() => {
            navigate('/inventory/start')
          }}
        >
          Débuter un inventaire
        </Button>
        <Link className="text-center text-sm text-slate-600 underline dark:text-slate-400" to="/admin">
          Espace administrateur
        </Link>
      </div>

      <RunsOverviewModal
        open={runsModalOpen}
        openRuns={openRunDetails}
        completedRuns={completedRunDetails}
        onClose={() => setRunsModalOpen(false)}
      />

      <ConflictZoneModal open={conflictModalOpen} zone={selectedZone} onClose={handleConflictModalClose} />
    </Page>
  )
}
