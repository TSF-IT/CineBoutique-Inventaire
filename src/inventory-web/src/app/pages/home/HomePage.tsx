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
import { SlidingPanel } from '../../components/SlidingPanel'
import { useAsync } from '../../hooks/useAsync'
import type { InventorySummary, Location } from '../../types/inventory'
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

const formatDateTime = (value: string) => {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return 'Non disponible'
  }
  return new Intl.DateTimeFormat('fr-FR', {
    dateStyle: 'short',
    timeStyle: 'short',
  }).format(date)
}

const describeCountType = (value: number) => {
  switch (value) {
    case 1:
      return 'Premier passage'
    case 2:
      return 'Second passage'
    case 3:
      return 'Contrôle'
    default:
      return `Type ${value}`
  }
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
  const [openRunsPanelOpen, setOpenRunsPanelOpen] = useState(false)
  const [conflictsPanelOpen, setConflictsPanelOpen] = useState(false)
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
  const conflictDetails = displaySummary?.conflictDetails ?? []
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
  const canShowOpenRunsPanel = hasOpenRuns && openRunDetails.length > 0
  const canShowConflictsPanel = hasConflicts && conflictDetails.length > 0

  const handleOpenRunsClick = useCallback(() => {
    if (canShowOpenRunsPanel) {
      setOpenRunsPanelOpen(true)
    }
  }, [canShowOpenRunsPanel])

  const handleConflictsClick = useCallback(() => {
    if (canShowConflictsPanel) {
      setConflictsPanelOpen(true)
    }
  }, [canShowConflictsPanel])

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
              disabled={!canShowOpenRunsPanel}
              className={clsx(
                'flex flex-col rounded-2xl border border-brand-300 bg-brand-100/70 p-5 text-left transition dark:border-brand-500/30 dark:bg-brand-500/10',
                canShowOpenRunsPanel
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
              {canShowOpenRunsPanel && (
                <p className="mt-1 text-xs text-brand-700/80 dark:text-brand-200/80">Touchez pour voir le détail</p>
              )}
              <p className="mt-3 text-xs text-brand-700/80 dark:text-brand-100/70">
                Comptages terminés : {completedRuns} / {totalExpected || 0}
              </p>
            </button>
            <button
              type="button"
              onClick={handleConflictsClick}
              disabled={!canShowConflictsPanel}
              className={clsx(
                'flex flex-col rounded-2xl border p-5 text-left transition',
                hasConflicts
                  ? 'border-rose-300 bg-rose-100/70 dark:border-rose-500/40 dark:bg-rose-500/10'
                  : 'border-emerald-300 bg-emerald-100/70 dark:border-emerald-500/30 dark:bg-emerald-500/10',
                canShowConflictsPanel
                  ? 'cursor-pointer focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-offset-2 focus-visible:ring-rose-500'
                  : 'cursor-default'
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
              {canShowConflictsPanel && (
                <p className="mt-1 text-xs text-rose-700/80 dark:text-rose-200/70">Touchez pour voir le détail</p>
              )}
            </button>
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

      <SlidingPanel open={openRunsPanelOpen} title="Comptages en cours" onClose={() => setOpenRunsPanelOpen(false)}>
        {openRunDetails.length === 0 ? (
          <p className="text-sm text-slate-600 dark:text-slate-300">Aucun comptage en cours.</p>
        ) : (
          <ul className="divide-y divide-slate-200 dark:divide-slate-800">
            {openRunDetails.map((run) => {
              const operator = run.operatorDisplayName?.trim() || '—'
              return (
                <li key={run.runId} className="py-3">
                  <p className="text-sm font-semibold text-slate-900 dark:text-white">
                    {run.locationCode} – {run.locationLabel}
                  </p>
                  <p className="mt-1 text-sm text-slate-600 dark:text-slate-300">
                    {describeCountType(run.countType)} · {operator} · démarré {formatDateTime(run.startedAtUtc)}
                  </p>
                </li>
              )
            })}
          </ul>
        )}
      </SlidingPanel>

      <SlidingPanel open={conflictsPanelOpen} title="Conflits à résoudre" onClose={() => setConflictsPanelOpen(false)}>
        {conflictDetails.length === 0 ? (
          <p className="text-sm text-slate-600 dark:text-slate-300">Aucun conflit actif.</p>
        ) : (
          <ul className="divide-y divide-slate-200 dark:divide-slate-800">
            {conflictDetails.map((conflict) => (
              <li key={conflict.conflictId} className="py-3">
                <p className="text-sm font-semibold text-slate-900 dark:text-white">
                  {conflict.locationCode} · {conflict.locationLabel}
                </p>
                <p className="text-sm text-slate-600 dark:text-slate-300">
                  {describeCountType(conflict.countType)} — signalé le {formatDateTime(conflict.createdAtUtc)}
                </p>
                <p className="text-sm text-slate-600 dark:text-slate-300">
                  Opérateur : {conflict.operatorDisplayName?.trim() || 'Non renseigné'}
                </p>
              </li>
            ))}
          </ul>
        )}
      </SlidingPanel>
    </Page>
  )
}
