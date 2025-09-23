import { useCallback, useMemo } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { fetchInventorySummary } from '../../api/inventoryApi'
import { Button } from '../../components/ui/Button'
import { Card } from '../../components/Card'
import { ErrorPanel } from '../../components/ErrorPanel'
import { EmptyState } from '../../components/EmptyState'
import { LoadingIndicator } from '../../components/LoadingIndicator'
import { Page } from '../../components/Page'
import { SectionTitle } from '../../components/SectionTitle'
import { useAsync } from '../../hooks/useAsync'
import type { InventorySummary } from '../../types/inventory'
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
        ? `${detail}\nVérifie que l'API répond sur ${error.url ?? 'http://localhost:8080/api'}.`
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
  const onError = useCallback((e: unknown) => {
    const err = e as HttpError
    console.error('[home] http error', err)
  }, [])

  const { data, loading, error, execute } = useAsync(fetchInventorySummary, [], {
    initialValue: null,
    onError,
  })

  const handleRetry = useCallback(() => {
    void execute()
  }, [execute])

  const errorDetails = useMemo(() => describeError(error), [error])

  const hasContextInfos = useMemo(() => {
    if (!data) {
      return false
    }
    return (data.activeSessions ?? 0) > 0 || (data.openRuns ?? 0) > 0 || Boolean(data.lastActivityUtc)
  }, [data])

  const displaySummary: InventorySummary | null = data ?? null

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
        <SectionTitle>État des inventaires</SectionTitle>
        {loading && <LoadingIndicator label="Chargement des indicateurs" />}
        {!loading && errorDetails && (
          <ErrorPanel title={errorDetails.title} details={errorDetails.details} actionLabel="Réessayer" onAction={handleRetry} />
        )}
        {!loading && !errorDetails && hasContextInfos && displaySummary && (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <div className="rounded-2xl border border-brand-300 bg-brand-100/70 p-5 dark:border-brand-500/30 dark:bg-brand-500/10">
              <p className="text-sm uppercase text-brand-600 dark:text-brand-200">Sessions actives</p>
              <p className="mt-2 text-4xl font-bold text-brand-700 dark:text-white">{displaySummary.activeSessions}</p>
            </div>
            <div className="rounded-2xl border border-emerald-300 bg-emerald-100/70 p-5 dark:border-emerald-500/40 dark:bg-emerald-500/10">
              <p className="text-sm uppercase text-emerald-700 dark:text-emerald-200">Runs ouverts</p>
              <p className="mt-2 text-4xl font-bold text-emerald-700 dark:text-emerald-100">{displaySummary.openRuns}</p>
            </div>
            <div className="rounded-2xl border border-slate-300 bg-slate-100/70 p-5 dark:border-slate-600/60 dark:bg-slate-900/40">
              <p className="text-sm uppercase text-slate-600 dark:text-slate-300">Dernière activité</p>
              <p className="mt-2 text-lg font-semibold text-slate-800 dark:text-white">
                {formatLastActivity(displaySummary.lastActivityUtc)}
              </p>
            </div>
          </div>
        )}
        {!loading && !errorDetails && (!displaySummary || !hasContextInfos) && (
          <EmptyState
            title="Pas encore de données"
            description="Le résumé d'inventaire n'est pas disponible pour le moment. Vous pourrez le consulter dès qu'un comptage aura débuté."
          />
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
    </Page>
  )
}
