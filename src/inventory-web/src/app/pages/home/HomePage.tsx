import { clsx } from 'clsx'
import { useCallback, useEffect, useMemo, useState } from 'react'
import type { MouseEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'

import {
  fetchInventorySummary,
  fetchLocationSummaries,
  fetchLocations,
  resetShopInventory,
} from '../../api/inventoryApi'
import type { ResetShopInventoryResponse } from '../../api/inventoryApi'
import { Card } from '../../components/Card'
import { ConflictZoneModal } from '../../components/Conflicts/ConflictZoneModal'
import { ErrorPanel } from '../../components/ErrorPanel'
import { LoadingIndicator } from '../../components/LoadingIndicator'
import { Page } from '../../components/Page'
import { CompletedRunsModal } from '../../components/Runs/CompletedRunsModal'
import { OpenRunsModal } from '../../components/Runs/OpenRunsModal'
import { SectionTitle } from '../../components/SectionTitle'
import { Button } from '../../components/ui/Button'
import { useInventory } from '../../contexts/InventoryContext'
import { useAsync } from '../../hooks/useAsync'
import type { ConflictZoneSummary, InventorySummary, Location, OpenRunSummary } from '../../types/inventory'
import { CountType } from '../../types/inventory'

import { BackToShopSelectionLink } from '@/app/components/BackToShopSelectionLink'
import { modalOverlayClassName } from '@/app/components/Modal/modalOverlayClassName'
import { modalContainerStyle } from '@/app/components/Modal/modalContainerStyle'
import { ModalPortal } from '@/app/components/Modal/ModalPortal'
import { ProductsCountCard } from '@/components/products/ProductsCountCard'
import { ProductsModal } from '@/components/products/ProductsModal'
import { API_BASE } from '@/lib/api/config'
import type { HttpError } from '@/lib/api/http'
import { useShop } from '@/state/ShopContext'
import type { LocationSummary } from '@/types/summary'

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

const normalize = (value: string | null | undefined) => value?.trim().toLowerCase() ?? ''

const toDateOrNull = (value: string | null | undefined): Date | null => {
  if (!value) {
    return null
  }
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? null : date
}

const findLocationCandidate = (
  locations: Location[],
  identifiers: { locationId?: string | null; locationCode?: string | null },
): Location | null => {
  const idKey = normalize(identifiers.locationId)
  const codeKey = normalize(identifiers.locationCode)
  if (!Array.isArray(locations) || locations.length === 0) {
    return null
  }
  if (idKey) {
    const byId = locations.find((item) => normalize(item.id) === idKey)
    if (byId) {
      return byId
    }
  }
  if (codeKey) {
    const byCode = locations.find((item) => normalize(item.code) === codeKey)
    if (byCode) {
      return byCode
    }
  }
  return null
}

const createFallbackLocationFromRun = (run: OpenRunSummary): Location => {
  const startedAt = toDateOrNull(run.startedAtUtc)
  return {
    id: run.locationId,
    code: run.locationCode,
    label: run.locationLabel,
    isBusy: true,
    busyBy: run.ownerDisplayName ?? null,
    activeRunId: run.runId,
    activeCountType: run.countType,
    activeStartedAtUtc: startedAt,
    countStatuses: [
      {
        countType: run.countType,
        status: 'in_progress',
        runId: run.runId,
        ownerDisplayName: run.ownerDisplayName ?? null,
        ownerUserId: run.ownerUserId ?? null,
        startedAtUtc: startedAt,
        completedAtUtc: null,
      },
    ],
    disabled: false,
  }
}

const createFallbackLocationFromZone = (zone: ConflictZoneSummary): Location => ({
  id: zone.locationId,
  code: zone.locationCode,
  label: zone.locationLabel,
  disabled: false,
  isBusy: false,
  busyBy: null,
  activeRunId: null,
  activeCountType: null,
  activeStartedAtUtc: null,
  countStatuses: [],
})

const summaryCardBaseClasses =
  'flex w-full min-h-[192px] flex-col gap-3 rounded-xl border p-5 text-left shadow-elev-1 transition'

const ZONE_COMPLETION_TYPES: CountType[] = [CountType.Count1, CountType.Count2]

const isRunOwnedByUser = (
  run: OpenRunSummary,
  selectedUserId: string | null,
  selectedUserDisplayName: string | null,
) => {
  const ownerUserId = run.ownerUserId?.trim() ?? null
  const ownerDisplayName = run.ownerDisplayName?.trim() ?? null
  if (ownerUserId && selectedUserId) {
    return ownerUserId === selectedUserId
  }
  if (!ownerUserId && ownerDisplayName && selectedUserDisplayName) {
    return ownerDisplayName === selectedUserDisplayName
  }
  if (!ownerUserId && !ownerDisplayName) {
    return Boolean(selectedUserId || selectedUserDisplayName)
  }
  return false
}

export const HomePage = () => {
  const navigate = useNavigate()
  const { shop, isLoaded } = useShop()
  const shopId = shop?.id ?? null
  const {
    selectedUser,
    countType,
    location: selectedLocation,
    sessionId,
    items,
    setLocation,
    setCountType,
    setSessionId,
    clearSession,
  } = useInventory()
  const [openRunsModalOpen, setOpenRunsModalOpen] = useState(false)
  const [completedRunsModalOpen, setCompletedRunsModalOpen] = useState(false)
  const [conflictModalOpen, setConflictModalOpen] = useState(false)
  const [selectedZone, setSelectedZone] = useState<ConflictZoneSummary | null>(null)
  const [showProducts, setShowProducts] = useState(false)
  const [catalogProductCount, setCatalogProductCount] = useState<number | null>(null)
  const [catalogWarningOpen, setCatalogWarningOpen] = useState(false)
  const [resetModalOpen, setResetModalOpen] = useState(false)
  const [resettingInventory, setResettingInventory] = useState(false)
  const [resetError, setResetError] = useState<string | null>(null)
  const [lastResetSummary, setLastResetSummary] = useState<ResetShopInventoryResponse | null>(null)
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

  const scannedItemsCount = items.length
  const normalizedSessionId = typeof sessionId === 'string' ? sessionId.trim() : ''
  const hasSelectedLocation = Boolean(selectedLocation)

  const handleProductsCardStateChange = useCallback((nextState: { count: number; hasCatalog: boolean } | null) => {
    setCatalogProductCount(typeof nextState?.count === 'number' ? nextState.count : null)
  }, [])

  const handleCloseCatalogWarning = useCallback(() => {
    setCatalogWarningOpen(false)
  }, [])

  const handleNavigateToCatalogImport = useCallback(() => {
    setCatalogWarningOpen(false)
    navigate('/admin')
  }, [navigate])

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
    if (!shopId) {
      return Promise.resolve<Location[]>([])
    }
    return fetchLocations(shopId, { includeDisabled: true })
  }, [shopId])

  const loadSummaries = useCallback(() => {
    if (!shopId) {
      return Promise.resolve<LocationSummary[]>([])
    }
    return fetchLocationSummaries(shopId)
  }, [shopId])

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

    if (!shopId) {
      setSummaryData(null)
      setLocationsData([])
      setLocationSummariesData([])
      navigate('/select-shop', { replace: true })
      return
    }

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
    shopId,
  ])

  useEffect(() => {
    setLastResetSummary(null)
  }, [shopId])

  const handleRetry = useCallback(() => {
    if (!shopId) {
      return
    }
    void executeSummary()
    void executeLocations()
    void executeLocationSummaries()
  }, [executeLocationSummaries, executeLocations, executeSummary, shopId])

  const handleChangeShop = useCallback(() => {
    const shouldPreserveSnapshot =
      scannedItemsCount > 0 || normalizedSessionId.length > 0 || countType !== null || hasSelectedLocation

    if (shouldPreserveSnapshot) {
      clearSession({ preserveSnapshot: true })
    }
    navigate('/select-user', { state: { redirectTo: '/' } })
  }, [
    clearSession,
    countType,
    hasSelectedLocation,
    navigate,
    normalizedSessionId,
    scannedItemsCount,
  ])

  const handleStartInventory = useCallback(() => {
    if (catalogProductCount === 0) {
      setCatalogWarningOpen(true)
      return
    }
    navigate('/inventory/location')
  }, [catalogProductCount, navigate])

  const combinedError = summaryError ?? locationsError ?? locationSummariesError
  const combinedLoading = summaryLoading || locationsLoading || locationSummariesLoading

  const errorDetails = useMemo(() => describeError(combinedError), [combinedError])

  const displaySummary: InventorySummary | null = summaryData ?? null
  const openRunsCount = displaySummary?.openRuns ?? 0
  const conflictCount = displaySummary?.conflicts ?? 0
  const openRunDetails = useMemo(() => displaySummary?.openRunDetails ?? [], [displaySummary])
  const completedRunDetails = useMemo(() => displaySummary?.completedRunDetails ?? [], [displaySummary])
  const conflictZones = useMemo(() => displaySummary?.conflictZones ?? [], [displaySummary])
  const locations = useMemo(() => locationsData ?? [], [locationsData])
  const activeLocations = useMemo(
    () => locations.filter((location) => !location.disabled),
    [locations],
  )
  const locationSummaries = useMemo(() => locationSummariesData ?? [], [locationSummariesData])
  const completedZonesFromLocations = useMemo(() => {
    if (activeLocations.length === 0) {
      return 0
    }

    return activeLocations.reduce((acc, location) => {
      const statusesByType = new Map<number, Location['countStatuses'][number]>()
      for (const status of location.countStatuses ?? []) {
        const type = typeof status.countType === 'number' ? (status.countType as CountType) : null
        if (type && ZONE_COMPLETION_TYPES.includes(type)) {
          statusesByType.set(type, status)
        }
      }

      const isZoneCompleted = ZONE_COMPLETION_TYPES.every((type) => statusesByType.get(type)?.status === 'completed')
      return acc + (isZoneCompleted ? 1 : 0)
    }, 0)
  }, [activeLocations])

  const completedCountsFromLocations = useMemo(() => {
    if (activeLocations.length === 0) {
      return 0
    }

    return activeLocations.reduce((total, location) => {
      const statuses = location.countStatuses ?? []
      const completedForLocation = ZONE_COMPLETION_TYPES.reduce((count, type) => {
        const status = statuses.find((item) => Number(item.countType) === Number(type))
        return count + (status?.status === 'completed' ? 1 : 0)
      }, 0)

      return total + completedForLocation
    }, 0)
  }, [activeLocations])

  const completedZones = useMemo(() => {
    if (activeLocations.length > 0) {
      return completedZonesFromLocations
    }

    const summaryValue = typeof displaySummary?.completedRuns === 'number' ? displaySummary.completedRuns : 0
    return Math.max(summaryValue, 0)
  }, [activeLocations.length, completedZonesFromLocations, displaySummary])

  const completedCounts = useMemo(() => {
    if (activeLocations.length > 0) {
      return completedCountsFromLocations
    }

    return completedRunDetails.reduce((total, run) => {
      if (ZONE_COMPLETION_TYPES.includes(run.countType)) {
        return total + 1
      }
      return total
    }, 0)
  }, [activeLocations.length, completedCountsFromLocations, completedRunDetails])

  const totalExpected = useMemo(() => activeLocations.length, [activeLocations.length])
  const totalExpectedCounts = useMemo(() => {
    if (activeLocations.length > 0) {
      return activeLocations.length * ZONE_COMPLETION_TYPES.length
    }

    if (locationSummaries.length > 0) {
      return locationSummaries.length * ZONE_COMPLETION_TYPES.length
    }

    const summaryValue = typeof displaySummary?.completedRuns === 'number' ? displaySummary.completedRuns : 0
    if (summaryValue > 0) {
      return summaryValue
    }

    return completedCounts > 0 ? completedCounts : 0
  }, [activeLocations.length, completedCounts, displaySummary, locationSummaries.length])
  const hasOpenRuns = openRunsCount > 0
  const hasConflicts = conflictCount > 0
  const completedZonesLabel = useMemo(() => {
    const completedPlural =
      completedZones > 1 ? 'zones terminées' : completedZones === 1 ? 'zone terminée' : 'zones terminées'
    if (totalExpected > 0) {
      return `${completedZones} ${completedPlural} sur ${totalExpected}`
    }

    if (completedZones <= 0) {
      return 'Aucune zone terminée'
    }

    return `${completedZones} ${completedPlural}`
  }, [completedZones, totalExpected])
  const completedCountsLabel = useMemo(() => {
    if (totalExpectedCounts > 0) {
      return `${completedCounts}/${totalExpectedCounts}`
    }

    if (completedCounts <= 0) {
      return 'Aucun comptage terminé'
    }

    return String(completedCounts)
  }, [completedCounts, totalExpectedCounts])
  const hasCompletedZones = completedZones > 0
  const hasCompletedCounts = completedCounts > 0
  const canOpenOpenRunsModal = openRunDetails.length > 0
  const canOpenCompletedRunsModal = completedRunDetails.length > 0
  const canOpenConflicts = hasConflicts && conflictZones.length > 0
  const hasLocationSummaries = locationSummaries.length > 0
  const effectiveZoneTarget = useMemo(() => {
    if (totalExpected > 0) {
      return totalExpected
    }
    if (completedZones > 0) {
      return completedZones
    }
    return 0
  }, [completedZones, totalExpected])
  const isInventoryComplete =
    effectiveZoneTarget > 0 && completedZones >= effectiveZoneTarget && !hasConflicts && !hasOpenRuns
  const selectedUserId = selectedUser?.id?.trim() ?? null
  const selectedUserDisplayName = selectedUser?.displayName?.trim() ?? null
  const ownedRunIds = useMemo(() => {
    if (!selectedUserId && !selectedUserDisplayName) {
      return [] as string[]
    }
    return openRunDetails
      .filter((run) => isRunOwnedByUser(run, selectedUserId, selectedUserDisplayName))
      .map((run) => run.runId)
  }, [openRunDetails, selectedUserDisplayName, selectedUserId])
  const hasOwnedOpenRuns = ownedRunIds.length > 0
  const ownedRunsLabel = useMemo(() => {
    if (!hasOwnedOpenRuns) {
      return null
    }
    const count = ownedRunIds.length
    return count <= 1 ? 'Vous avez un comptage en cours' : `Vous avez ${count} comptages en cours`
  }, [hasOwnedOpenRuns, ownedRunIds.length])

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

  const handleDownloadReport = useCallback((url: string | null) => {
    if (!url) {
      return
    }
    window.open(url, '_blank', 'noopener')
  }, [])

  const handleOpenResetModal = useCallback(() => {
    setResetError(null)
    setResetModalOpen(true)
  }, [])

  const handleCloseResetModal = useCallback(() => {
    if (resettingInventory) {
      return
    }
    setResetModalOpen(false)
  }, [resettingInventory])

  const handleConfirmReset = useCallback(async () => {
    if (!shopId) {
      return
    }
    setResetError(null)
    setResettingInventory(true)
    try {
      const result = await resetShopInventory(shopId)
      setResetModalOpen(false)
      setLastResetSummary(result)
      clearSession()
      await executeSummary()
      await executeLocations()
      await executeLocationSummaries()
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Impossible de relancer l’inventaire.'
      setResetError(message)
    } finally {
      setResettingInventory(false)
    }
  }, [clearSession, executeLocationSummaries, executeLocations, executeSummary, shopId])

  const dismissResetSummary = useCallback(() => {
    setLastResetSummary(null)
  }, [])

  const openConflictModal = useCallback((zone: ConflictZoneSummary) => {
    setSelectedZone(zone)
    setConflictModalOpen(true)
  }, [])

  const handleConflictModalClose = useCallback(() => {
    setConflictModalOpen(false)
    setSelectedZone(null)
  }, [])

  const handleResumeRun = useCallback(
    (run: OpenRunSummary) => {
      if (!isLoaded) {
        return
      }

      if (!selectedUser) {
        navigate('/select-shop', { replace: true })
        return
      }

      const existingSessionId = sessionId?.trim() ?? null
      if (!existingSessionId || existingSessionId !== run.runId) {
        clearSession()
      }

      const candidate = findLocationCandidate(locations, {
        locationId: run.locationId,
        locationCode: run.locationCode,
      })
      const resolvedLocation = candidate ?? createFallbackLocationFromRun(run)
      setLocation(resolvedLocation)
      setCountType(run.countType)
      setSessionId(run.runId)
      setOpenRunsModalOpen(false)
      navigate('/inventory/session')
    },
    [
      clearSession,
      isLoaded,
      locations,
      navigate,
      selectedUser,
      sessionId,
      setCountType,
      setLocation,
      setSessionId,
    ],
  )

  const handleStartConflictCount = useCallback(
    (zone: ConflictZoneSummary, nextCountType: number) => {
      if (!selectedUser) {
        navigate('/select-shop', { replace: true })
        return
      }

      clearSession()
      const candidate = findLocationCandidate(locations, {
        locationId: zone.locationId,
        locationCode: zone.locationCode,
      })
      const resolvedLocation = candidate ?? createFallbackLocationFromZone(zone)
      setLocation(resolvedLocation)
      setCountType(nextCountType)
      setSessionId(null)
      setConflictModalOpen(false)
      setSelectedZone(null)
      navigate('/inventory/session')
    },
    [
      clearSession,
      locations,
      navigate,
      selectedUser,
      setCountType,
      setLocation,
      setSessionId,
    ],
  )

  const shopDisplayName = shop?.name?.trim()
  const currentYear = new Date().getFullYear()
  const zonesReportUrl = useMemo(() => {
    if (!shopId) {
      return null
    }
    return `${API_BASE}/shops/${encodeURIComponent(shopId)}/reports/inventory/zones.csv`
  }, [shopId])
  const skuReportUrl = useMemo(() => {
    if (!shopId) {
      return null
    }
    return `${API_BASE}/shops/${encodeURIComponent(shopId)}/reports/inventory/sku.csv`
  }, [shopId])
  const resetSummaryMessage = useMemo(() => {
    if (!lastResetSummary) {
      return null
    }
    const { zonesCleared, runsCleared, linesCleared, conflictsCleared } = lastResetSummary
    const formatCount = (value: number, singular: string, plural: string) =>
      `${value} ${value > 1 ? plural : singular}`
    const parts = [
      formatCount(zonesCleared, 'zone réinitialisée', 'zones réinitialisées'),
      formatCount(runsCleared, 'comptage supprimé', 'comptages supprimés'),
      formatCount(linesCleared, 'ligne supprimée', 'lignes supprimées'),
      formatCount(conflictsCleared, 'conflit supprimé', 'conflits supprimés'),
    ]
    return parts.join(' · ')
  }, [lastResetSummary])

  return (
    <Page
      headerAction={
        <div className="flex items-center gap-3 sm:self-start">
          <BackToShopSelectionLink
            to="/select-user"
            label="Retour au choix de l’utilisateur"
            onClick={handleChangeShop}
          />
          <Link
            to="/admin"
            className="inline-flex h-11 w-11 min-h-[var(--tap-min)] items-center justify-center rounded-full border border-(--cb-border-soft) bg-(--cb-surface-soft) text-lg font-semibold text-brand-600 shadow-panel-soft transition-all duration-200 hover:-translate-y-0.5 hover:text-brand-500 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-300 focus-visible:ring-offset-2 focus-visible:ring-offset-(--cb-surface) dark:text-brand-200 dark:hover:text-brand-100"
            aria-label="Accéder à l’espace administrateur"
          >
            <span aria-hidden="true">⚙️</span>
            <span className="sr-only">Accéder à l’espace administrateur</span>
          </Link>
        </div>
      }
    >
      <section className="flex flex-col gap-4">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
          <div className="flex-1">
            <p className="mt-1 text-xs font-semibold uppercase tracking-[0.4em] text-brand-500/90 dark:text-brand-200/90">
              {shopDisplayName ?? 'CinéBoutique'}
            </p>
            <h1 className="mt-2 text-3xl font-semibold tracking-tight text-(--cb-text)">Inventaire {currentYear}</h1>
            <p className="mt-2 max-w-xl text-sm leading-relaxed text-(--cb-muted)">
              Visualisez les conflits, suivez les comptages et le catalogue ; démarrez ou reprenez vos sessions en toute simplicité.
            </p>
          </div>
          <div className="hidden text-right text-xs text-(--cb-muted) sm:block">
            <p>Utilisateur : {selectedUser?.displayName ?? '–'}</p>
            <p>Zone : {selectedLocation?.label ?? '–'}</p>
            <p>Comptage : {countType ?? '–'}</p>
          </div>
        </div>
      </section>

      {lastResetSummary && resetSummaryMessage && (
        <div className="flex items-start justify-between gap-4 rounded-3xl border border-brand-300 bg-brand-50 px-5 py-4 text-sm text-brand-700 shadow-panel-soft dark:border-brand-500/40 dark:bg-brand-500/10 dark:text-brand-100">
          <div>
            <p className="font-semibold">Inventaire relancé</p>
            <p className="mt-1">{resetSummaryMessage}</p>
          </div>
          <button
            type="button"
            onClick={dismissResetSummary}
            className="rounded-full border border-brand-200 p-1 text-brand-600 transition hover:bg-brand-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500 focus-visible:ring-offset-2 dark:border-brand-500/40 dark:text-brand-100 dark:hover:bg-brand-500/20"
            aria-label="Fermer le message de confirmation de reset"
          >
            <span aria-hidden="true">✕</span>
          </button>
        </div>
      )}

      <Card className="flex flex-col gap-4">
        <SectionTitle>État de l’inventaire</SectionTitle>
        {combinedLoading && <LoadingIndicator label="Chargement des indicateurs" />}
        {!combinedLoading && errorDetails && (
          <ErrorPanel title={errorDetails.title} details={errorDetails.details} actionLabel="Réessayer" onAction={handleRetry} />
        )}
        {!combinedLoading && !errorDetails && isInventoryComplete && (
          <div className="flex flex-col items-center gap-6 rounded-3xl border border-emerald-300 bg-emerald-50/80 px-6 py-8 text-center shadow-panel-soft dark:border-emerald-500/40 dark:bg-emerald-500/10">
            <div className="flex flex-col items-center gap-3">
              <p className="text-xs font-semibold uppercase tracking-[0.4em] text-emerald-700/80 dark:text-emerald-200/80">
                Inventaire
              </p>
              <h2 className="flex items-center gap-2 text-3xl font-semibold tracking-tight text-emerald-800 dark:text-emerald-100">
                Inventaire terminé <span aria-hidden="true">🎉</span>
              </h2>
              <p className="max-w-2xl text-sm text-emerald-700/90 dark:text-emerald-100/80">
                Toutes les zones ont été comptées et aucun conflit n’est en attente. Vous pouvez exporter les rapports
                ou relancer un nouvel inventaire pour repartir de zéro.
              </p>
            </div>
            <div className="flex w-full flex-col gap-3 sm:flex-row sm:justify-center">
              <Button
                variant="secondary"
                fullWidth
                onClick={() => handleDownloadReport(zonesReportUrl)}
                disabled={!zonesReportUrl}
              >
                Rapport par zones
              </Button>
              <Button
                variant="secondary"
                fullWidth
                onClick={() => handleDownloadReport(skuReportUrl)}
                disabled={!skuReportUrl}
              >
                Rapport par Code/SKU
              </Button>
            </div>
            <Button
              className="w-full px-8 py-3 text-base sm:w-auto"
              onClick={handleOpenResetModal}
            >
              Relancer un inventaire
            </Button>
          </div>
        )}
        {!combinedLoading && !errorDetails && !isInventoryComplete && displaySummary && (
          <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
            {shopId && (
              <ProductsCountCard
                shopId={shopId}
                onStateChange={handleProductsCardStateChange}
                onOpen={() => setShowProducts(true)}
                className="border-product-300 bg-product-50/80 dark:border-product-500/40 dark:bg-product-500/10"
              />
            )}
            <div
              className={clsx(
                summaryCardBaseClasses,
                hasConflicts
                  ? 'border-rose-400 bg-rose-50/80 dark:border-rose-500/40 dark:bg-rose-500/10'
                  : 'border-amber-200 bg-amber-50/60 dark:border-amber-500/30 dark:bg-amber-500/5'
              )}
            >
              <p
                className={clsx(
                  'text-sm uppercase',
                  hasConflicts
                    ? 'text-rose-900 dark:text-rose-100'
                    : 'text-amber-700 dark:text-amber-200'
                )}
              >
                Conflits
              </p>
              <p
                className={clsx(
                  'mt-2 font-semibold',
                  hasConflicts
                    ? 'text-4xl text-rose-900 dark:text-rose-100'
                    : 'text-lg text-amber-800 dark:text-amber-100'
                )}
              >
                {hasConflicts ? conflictCount : 'Aucun conflit'}
              </p>
              {canOpenConflicts && (
                <p
                  className={clsx(
                    'mt-1 text-xs',
                    hasConflicts
                      ? 'text-rose-900/80 dark:text-rose-200/70'
                      : 'text-amber-800/80 dark:text-amber-200/70'
                  )}
                >
                  Touchez une zone pour voir le détail
                </p>
              )}
              <div className="mt-4 flex flex-col gap-2">
                {hasConflicts && conflictZones.length > 0 ? (
                  <ul
                    className={clsx(
                  'divide-y rounded-2xl border border-(--cb-border-soft) bg-(--cb-surface-soft)',
                      hasConflicts
                        ? 'divide-rose-200/70 border-rose-200/70 dark:divide-rose-500/40 dark:border-rose-500/30 dark:bg-rose-500/10'
                        : 'divide-amber-200/70 border-amber-200/70 dark:divide-amber-500/40 dark:border-amber-500/30 dark:bg-amber-500/10'
                    )}
                  >
                    {conflictZones.map((zone) => (
                      <li key={zone.locationId}>
                        <button
                          type="button"
                          onClick={() => openConflictModal(zone)}
                          className={clsx(
                            'flex w-full items-center justify-between gap-3 px-4 py-3 text-left text-sm transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-offset-2',
                            hasConflicts
                              ? 'hover:bg-rose-100/70 focus-visible:ring-rose-500 dark:hover:bg-rose-500/20'
                              : 'hover:bg-amber-100/70 focus-visible:ring-amber-500 dark:hover:bg-amber-500/20'
                          )}
                        >
                          <span
                            className={clsx(
                              'font-medium',
                              hasConflicts
                                ? 'text-rose-900 dark:text-rose-100'
                                : 'text-amber-900 dark:text-amber-100'
                            )}
                          >
                            {zone.locationCode} · {zone.locationLabel}
                          </span>
                          <span
                            className={clsx(
                              'text-xs font-semibold uppercase tracking-wide',
                              hasConflicts
                                ? 'text-rose-900 dark:text-rose-100'
                                : 'text-amber-800 dark:text-amber-200'
                            )}
                          >
                            {zone.conflictLines} réf.
                          </span>
                        </button>
                      </li>
                    ))}
                  </ul>
                ) : hasConflicts ? (
                  <p className="rounded-2xl border border-rose-200/70 bg-rose-100/60 px-4 py-3 text-xs text-rose-900 dark:border-rose-500/40 dark:bg-rose-500/10 dark:text-rose-200">
                    Impossible d’afficher le détail des zones en conflit.
                  </p>
                ) : null}
              </div>
            </div>
            <button
              type="button"
              onClick={handleOpenCompletedRunsClick}
              disabled={!canOpenCompletedRunsModal}
              className={clsx(
                summaryCardBaseClasses,
                'border-emerald-300 bg-emerald-50/80 dark:border-emerald-500/40 dark:bg-emerald-500/10',
                canOpenCompletedRunsModal
                  ? 'cursor-pointer hover:shadow-elev-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald-500 focus-visible:ring-offset-2'
                  : 'cursor-default'
              )}
            >
              <p className="text-sm uppercase text-emerald-700 dark:text-emerald-200">Comptages terminés</p>
              <div className="mt-2 flex flex-col gap-1">
                <p
                  className={clsx(
                    'font-semibold',
                    hasCompletedCounts
                      ? 'text-4xl leading-tight text-emerald-800 dark:text-emerald-100'
                      : 'text-lg text-emerald-700 dark:text-emerald-200'
                  )}
                >
                  {completedCountsLabel}
                </p>
                <p
                  className={clsx(
                    'text-sm font-medium',
                    hasCompletedZones
                      ? 'text-emerald-800 dark:text-emerald-100'
                      : 'text-emerald-700 dark:text-emerald-200'
                  )}
                >
                  {completedZonesLabel}
                </p>
              </div>
              {canOpenCompletedRunsModal && (
                <p className="mt-1 text-xs text-emerald-700/80 dark:text-emerald-200/80">Touchez pour voir le détail</p>
              )}
            </button>
            <button
              type="button"
              onClick={handleOpenRunsClick}
              disabled={!canOpenOpenRunsModal}
              className={clsx(
                summaryCardBaseClasses,
                'border-brand-300 bg-brand-50/80 dark:border-brand-500/40 dark:bg-brand-500/10',
                canOpenOpenRunsModal
                  ? 'cursor-pointer hover:shadow-elev-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500 focus-visible:ring-offset-2'
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
              {hasOwnedOpenRuns && ownedRunsLabel && (
                <p className="mt-2 inline-flex items-center gap-2 text-xs font-semibold text-brand-700/90 dark:text-brand-100">
                  <span aria-hidden="true">👤</span>
                  <span>{ownedRunsLabel}</span>
                </p>
              )}
              {canOpenOpenRunsModal && (
                <p className="mt-1 text-xs text-brand-700/80 dark:text-brand-200/80">Touchez pour voir le détail</p>
              )}
            </button>
          </div>
        )}
        {!combinedLoading && !errorDetails && !displaySummary && (
          <p className="text-sm text-(--cb-muted)">
            Les indicateurs ne sont pas disponibles pour le moment.
          </p>
        )}
        {!combinedLoading && !errorDetails && !isInventoryComplete && !hasLocationSummaries && (
          <div className="rounded-2xl border border-dashed border-(--cb-border-soft) bg-(--cb-surface-soft) p-5 text-sm text-(--cb-muted) shadow-panel-soft">
            <p className="font-medium text-(--cb-text)">
              Aucun comptage en cours pour cette boutique.
            </p>
            <p className="mt-2 text-sm text-(--cb-muted)">
              Utilisez le bouton &laquo;&nbsp;Débuter un comptage&nbsp;&raquo; pour lancer une nouvelle session et suivre les zones en
              temps réel.
            </p>
          </div>
        )}
      </Card>

      <div className="flex flex-col gap-4">
        <Button
          fullWidth
          className="py-5 text-lg"
          onClick={handleStartInventory}
          disabled={isInventoryComplete}
        >
          Débuter un comptage
        </Button>
      </div>

      <OpenRunsModal
        open={openRunsModalOpen}
        openRuns={openRunDetails}
        onClose={() => setOpenRunsModalOpen(false)}
        ownedRunIds={ownedRunIds}
        onResumeRun={hasOwnedOpenRuns ? handleResumeRun : undefined}
      />

      <CompletedRunsModal
        open={completedRunsModalOpen}
        completedRuns={completedRunDetails}
        onClose={() => setCompletedRunsModalOpen(false)}
      />

      <ConflictZoneModal
        open={conflictModalOpen}
        zone={selectedZone}
        onClose={handleConflictModalClose}
        onStartExtraCount={selectedZone ? handleStartConflictCount : undefined}
      />
      {shopId && (
        <ProductsModal open={showProducts} onClose={() => setShowProducts(false)} shopId={shopId} />
      )}
      <CatalogWarningModal
        open={catalogWarningOpen}
        onClose={handleCloseCatalogWarning}
        onNavigateToImport={handleNavigateToCatalogImport}
      />
      <ResetInventoryModal
        open={resetModalOpen}
        loading={resettingInventory}
        error={resetError}
        onCancel={handleCloseResetModal}
        onConfirm={handleConfirmReset}
      />
    </Page>
  )
}

interface ResetInventoryModalProps {
  open: boolean
  loading: boolean
  error: string | null
  onConfirm: () => void
  onCancel: () => void
}

interface CatalogWarningModalProps {
  open: boolean
  onClose: () => void
  onNavigateToImport: () => void
}

const CatalogWarningModal = ({ open, onClose, onNavigateToImport }: CatalogWarningModalProps) => {
  const handleOverlayClick = useCallback(
    (event: MouseEvent<HTMLDivElement>) => {
      if (event.target === event.currentTarget) {
        onClose()
      }
    },
    [onClose],
  )

  if (!open) {
    return null
  }

  return (
    <ModalPortal>
      <div className={modalOverlayClassName} role="presentation" onClick={handleOverlayClick}>
        <div
          role="dialog"
          aria-modal="true"
          aria-labelledby="catalog-warning-title"
          className="relative flex w-full max-w-lg flex-col gap-4 overflow-y-auto rounded-3xl bg-white p-6 text-left shadow-2xl outline-none focus-visible:ring-2 focus-visible:ring-brand-500 dark:bg-slate-900"
          data-modal-container=""
          style={modalContainerStyle}
          tabIndex={-1}
        >
        <button
          type="button"
          onClick={onClose}
          className="absolute right-4 top-4 rounded-full border border-slate-200 p-1 text-slate-500 transition hover:bg-slate-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500 focus-visible:ring-offset-2 dark:border-slate-700 dark:text-slate-300 dark:hover:bg-slate-800"
          aria-label="Fermer l’alerte catalogue"
        >
          <span aria-hidden="true">✕</span>
        </button>
        <div className="space-y-3">
          <p className="text-xs font-semibold uppercase tracking-[0.3em] text-brand-600 dark:text-brand-200">Information</p>
          <h2 id="catalog-warning-title" className="text-2xl font-semibold text-slate-900 dark:text-white">
            Catalogue requis
          </h2>
          <p className="text-sm text-slate-600 dark:text-slate-300">
            Pour une meilleure expérience, veuillez préalablement importer un catalogue produits avant de démarrer un
            comptage.
          </p>
        </div>
        <div className="mt-4 flex flex-col gap-3 sm:flex-row sm:justify-end">
          <Button variant="secondary" onClick={onClose}>
            Fermer
          </Button>
          <Button onClick={onNavigateToImport}>Accéder à l’espace administrateur</Button>
        </div>
      </div>
      </div>
    </ModalPortal>
  )
}

const ResetInventoryModal = ({ open, loading, error, onConfirm, onCancel }: ResetInventoryModalProps) => {
  const handleOverlayClick = useCallback(
    (event: MouseEvent<HTMLDivElement>) => {
      if (event.target === event.currentTarget) {
        onCancel()
      }
    },
    [onCancel],
  )

  if (!open) {
    return null
  }

  return (
    <ModalPortal>
      <div className={modalOverlayClassName} role="presentation" onClick={handleOverlayClick}>
        <div
          role="dialog"
          aria-modal="true"
          aria-labelledby="reset-inventory-title"
          className="relative flex w-full max-w-lg flex-col gap-4 overflow-y-auto rounded-3xl bg-white p-6 text-left shadow-2xl outline-none focus-visible:ring-2 focus-visible:ring-brand-500 dark:bg-slate-900"
          data-modal-container=""
          style={modalContainerStyle}
          tabIndex={-1}
        >
        <button
          type="button"
          onClick={onCancel}
          className="absolute right-4 top-4 rounded-full border border-slate-200 p-1 text-slate-500 transition hover:bg-slate-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500 focus-visible:ring-offset-2 dark:border-slate-700 dark:text-slate-300 dark:hover:bg-slate-800"
          aria-label="Fermer la confirmation de reset"
        >
          <span aria-hidden="true">✕</span>
        </button>
        <div className="space-y-3">
          <p className="text-xs font-semibold uppercase tracking-[0.3em] text-brand-600 dark:text-brand-200">Confirmation</p>
          <h2 id="reset-inventory-title" className="text-2xl font-semibold text-slate-900 dark:text-white">
            Relancer un inventaire
          </h2>
          <p className="text-sm text-slate-600 dark:text-slate-300">
            Cette action supprimera tous les comptages, lignes et conflits de l’inventaire en cours pour cette boutique.
            Cette opération est irréversible. Confirmez-vous le reset&nbsp;?
          </p>
          {error && (
            <p className="rounded-xl border border-rose-200 bg-rose-50 px-3 py-2 text-sm text-rose-700 dark:border-rose-500/40 dark:bg-rose-500/10 dark:text-rose-200">
              {error}
            </p>
          )}
        </div>
        <div className="mt-4 flex flex-col gap-3 sm:flex-row sm:justify-end">
          <Button variant="secondary" onClick={onCancel} disabled={loading}>
            Annuler
          </Button>
          <Button variant="danger" onClick={onConfirm} disabled={loading}>
            {loading ? 'Réinitialisation...' : 'Confirmer le reset'}
          </Button>
        </div>
      </div>
      </div>
    </ModalPortal>
  )
}

