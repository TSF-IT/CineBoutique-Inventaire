// Modifications : chargement des zones pour le compteur terminé et panneau enrichi des runs ouverts.
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import clsx from 'clsx'
import { Link, useNavigate } from 'react-router-dom'
import { fetchInventorySummary, fetchLocationSummaries, fetchLocations } from '../../api/inventoryApi'
import { Button } from '../../components/ui/Button'
import { Card } from '../../components/Card'
import { ErrorPanel } from '../../components/ErrorPanel'
import { LoadingIndicator } from '../../components/LoadingIndicator'
import { Page } from '../../components/Page'
import { SectionTitle } from '../../components/SectionTitle'
import { ConflictZoneModal } from '../../components/Conflicts/ConflictZoneModal'
import { CompletedRunsModal } from '../../components/Runs/CompletedRunsModal'
import { OpenRunsModal } from '../../components/Runs/OpenRunsModal'
import { useAsync } from '../../hooks/useAsync'
import type { ConflictZoneSummary, InventorySummary, Location } from '../../types/inventory'
import type { LocationSummary } from '@/types/summary'
import type { HttpError } from '@/lib/api/http'
import { useShop } from '@/state/ShopContext'

const isHttpError = (value: unknown): value is HttpError =>
  typeof value === 'object' &&
  value !== null &&
  typeof (value as { status?: unknown }).status === 'number' &&
  typeof (value as { url?: unknown }).url === 'string'

const isProductNotFoundError = (error: unknown): error is HttpError =>
  isHttpError(error) && error.status === 404 && /\/products\//.test(error.url)

const describeError = (error: unknown): { title: string; details?: string } | null => {
  if (!error) {
    return null
  }
  if (isProductNotFoundError(error)) {
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
  const { shop, setShop, isLoaded } = useShop()
  const [openRunsModalOpen, setOpenRunsModalOpen] = useState(false)
  const [completedRunsModalOpen, setCompletedRunsModalOpen] = useState(false)
  const [conflictModalOpen, setConflictModalOpen] = useState(false)
  const [selectedZone, setSelectedZone] = useState<ConflictZoneSummary | null>(null)
  const lastLoadedShopIdRef = useRef<string | null>(null)
  const onError = useCallback((error: unknown) => {
    if (isProductNotFoundError(error)) {
      console.warn('[home] produit introuvable ignoré', error)
      return
    }
    console.error('[home] http error', error)
  }, [])

  const fetchSummarySafely = useCallback(async () => {
    try {
      return await fetchInventorySummary()
    } catch (error) {
      if (isProductNotFoundError(error)) {
        console.warn('[home] produit introuvable ignoré', error)
        return null
      }
      throw error
    }
  }, [])

  const {
    data: summaryData,
    loading: summaryLoading,
    error: summaryError,
    execute: executeSummary,
    setData: setSummaryData,
  } = useAsync(fetchSummarySafely, [fetchSummarySafely], {
    initialValue: null,
    onError,
    immediate: false,
  })

  const loadLocations = useCallback(() => {
    if (!shop?.id) {
      return Promise.resolve<Location[]>([])
    }
    return fetchLocations(shop.id)
  }, [shop?.id])

  const loadSummaries = useCallback(() => {
    if (!shop?.id) {
      return Promise.resolve<LocationSummary[]>([])
    }
    return fetchLocationSummaries(shop.id)
  }, [shop?.id])

  const {
    data: locationsData,
    loading: locationsLoading,
    error: locationsError,
    execute: executeLocations,
    setData: setLocationsData,
  } = useAsync<Location[]>(loadLocations, [loadLocations], {
    initialValue: [],
    onError,
    immediate: false,
  })

  const {
    data: locationSummariesData,
    loading: locationSummariesLoading,
    error: locationSummariesError,
    execute: executeLocationSummaries,
    setData: setLocationSummariesData,
  } = useAsync<LocationSummary[]>(loadSummaries, [loadSummaries], {
    initialValue: [],
    onError,
    immediate: false,
  })

  useEffect(() => {
    if (!isLoaded) {
      return
    }

    if (!shop?.id) {
      lastLoadedShopIdRef.current = null
      setSummaryData(null)
      setLocationsData([])
      setLocationSummariesData([])
      navigate('/select-shop', { replace: true })
      return
    }

    if (lastLoadedShopIdRef.current === shop.id) {
      return
    }

    lastLoadedShopIdRef.current = shop.id
    void executeSummary()
    void executeLocations()
    void executeLocationSummaries()
  }, [
    executeLocationSummaries,
    executeLocations,
    executeSummary,
    isLoaded,
    navigate,
    setLocationSummariesData,
    setLocationsData,
    setSummaryData,
    shop?.id,
  ])

  const handleRetry = useCallback(() => {
    if (!shop?.id) {
      return
    }
    void executeSummary()
    void executeLocations()
    void executeLocationSummaries()
  }, [executeLocationSummaries, executeLocations, executeSummary, shop?.id])

  const handleChangeShop = useCallback(() => {
    setShop(null)
    navigate('/select-shop')
  }, [navigate, setShop])

  const handleStartInventory = useCallback(() => {
    navigate('/inventory/location')
  }, [navigate])

  const combinedError = summaryError ?? locationsError ?? locationSummariesError
  const combinedLoading = summaryLoading || locationsLoading || locationSummariesLoading

  const errorDetails = useMemo(() => describeError(combinedError), [combinedError])

  const displaySummary: InventorySummary | null = summaryData ?? null
  const openRunsCount = displaySummary?.openRuns ?? 0
  const conflictCount = displaySummary?.conflicts ?? 0
  const openRunDetails = displaySummary?.openRunDetails ?? []
  const completedRunDetails = displaySummary?.completedRunDetails ?? []
  const conflictZones = useMemo(() => displaySummary?.conflictZones ?? [], [displaySummary])
  const locations = useMemo(() => locationsData ?? [], [locationsData])
  const locationSummaries = useMemo(() => locationSummariesData ?? [], [locationSummariesData])
  const completedRunsFromLocations = useMemo(() => {
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
  const completedRuns = useMemo(() => {
    if (!displaySummary) {
      return completedRunsFromLocations
    }

    const summaryValue = typeof displaySummary.completedRuns === 'number' ? displaySummary.completedRuns : 0
    return Math.max(summaryValue, completedRunsFromLocations)
  }, [completedRunsFromLocations, displaySummary])

  const totalExpected = useMemo(() => locations.length * 2, [locations.length])
  const hasOpenRuns = openRunsCount > 0
  const hasConflicts = conflictCount > 0
  const canOpenOpenRunsModal = openRunDetails.length > 0
  const canOpenCompletedRunsModal = completedRunDetails.length > 0
  const canOpenConflicts = hasConflicts && conflictZones.length > 0
  const hasLocationSummaries = locationSummaries.length > 0

  const handleOpenRunsClick = useCallback(() => {
    if (canOpenOpenRunsModal) {
      setOpenRunsModalOpen(true)
    }
  }, [canOpenOpenRunsModal])

  const handleOpenCompletedRunsClick = useCallback(() => {
    if (canOpenCompletedRunsModal) {
      setCompletedRunsModalOpen(true)
    }
  }, [canOpenCompletedRunsModal])

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
      <header className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
        <div className="flex flex-col gap-4">
          <p className="text-sm uppercase tracking-[0.3em] text-brand-600 dark:text-brand-200">CinéBoutique</p>
          <h1 className="text-4xl font-black leading-tight text-slate-900 dark:text-white sm:text-5xl">
            Inventaire simplifié
          </h1>
          <p className="max-w-xl text-base text-slate-600 dark:text-slate-300">
            Lancez un comptage en quelques gestes, scannez les produits depuis la caméra ou une douchette Bluetooth et
            assurez un suivi fiable de vos zones.
          </p>
        </div>
        <Button variant="secondary" onClick={handleChangeShop} className="self-start">
          Changer de boutique
        </Button>
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
              disabled={!canOpenOpenRunsModal}
              className={clsx(
                'flex flex-col rounded-2xl border border-brand-300 bg-brand-100/70 p-5 text-left transition dark:border-brand-500/30 dark:bg-brand-500/10',
                canOpenOpenRunsModal
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
              {canOpenOpenRunsModal && (
                <p className="mt-1 text-xs text-brand-700/80 dark:text-brand-200/80">Touchez pour voir le détail</p>
              )}
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
            <button
              type="button"
              onClick={handleOpenCompletedRunsClick}
              disabled={!canOpenCompletedRunsModal}
              className={clsx(
                'flex flex-col rounded-2xl border border-emerald-300 bg-emerald-100/70 p-5 text-left transition dark:border-emerald-500/40 dark:bg-emerald-500/10',
                canOpenCompletedRunsModal
                  ? 'cursor-pointer focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald-500 focus-visible:ring-offset-2'
                  : 'cursor-default'
              )}
            >
              <p className="text-sm uppercase text-emerald-700 dark:text-emerald-200">Comptages terminés</p>
              <p
                className={clsx(
                  'mt-2 font-semibold',
                  completedRuns > 0
                    ? 'text-4xl text-emerald-800 dark:text-emerald-100'
                    : 'text-lg text-emerald-700 dark:text-emerald-200'
                )}
              >
                {completedRuns > 0 ? completedRuns : 'Aucun comptage terminé'}
              </p>
              {canOpenCompletedRunsModal && (
                <p className="mt-1 text-xs text-emerald-700/80 dark:text-emerald-200/80">Touchez pour voir le détail</p>
              )}
              <p className="mt-3 text-xs text-emerald-700/80 dark:text-emerald-200/70">
                Progression : {completedRuns} / {totalExpected || 0}
              </p>
            </button>
          </div>
        )}
        {!combinedLoading && !errorDetails && !displaySummary && (
          <p className="text-sm text-slate-500 dark:text-slate-400">
            Les indicateurs ne sont pas disponibles pour le moment.
          </p>
        )}
        {!combinedLoading && !errorDetails && !hasLocationSummaries && (
          <div className="rounded-2xl border border-dashed border-slate-200 bg-white/70 p-5 text-sm text-slate-600 dark:border-slate-700 dark:bg-slate-900/30 dark:text-slate-300">
            <p className="font-medium text-slate-700 dark:text-slate-100">
              Aucun comptage en cours pour cette boutique.
            </p>
            <p className="mt-2 text-sm text-slate-500 dark:text-slate-400">
              Utilisez le bouton « Débuter un comptage » pour lancer une nouvelle session et suivre les zones en temps réel.
            </p>
          </div>
        )}
      </Card>

      <div className="flex flex-col gap-4">
        <Button
          fullWidth
          className="py-5 text-lg"
          onClick={handleStartInventory}
        >
          Débuter un comptage
        </Button>
        <Link className="text-center text-sm text-slate-600 underline dark:text-slate-400" to="/admin">
          Espace administrateur
        </Link>
      </div>

      <OpenRunsModal open={openRunsModalOpen} openRuns={openRunDetails} onClose={() => setOpenRunsModalOpen(false)} />

      <CompletedRunsModal
        open={completedRunsModalOpen}
        completedRuns={completedRunDetails}
        onClose={() => setCompletedRunsModalOpen(false)}
      />

      <ConflictZoneModal open={conflictModalOpen} zone={selectedZone} onClose={handleConflictModalClose} />
    </Page>
  )
}
