import { useMemo } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { fetchInventorySummary } from '../../api/inventoryApi'
import { Button } from '../../components/Button'
import { Card } from '../../components/Card'
import { EmptyState } from '../../components/EmptyState'
import { LoadingIndicator } from '../../components/LoadingIndicator'
import { Page } from '../../components/Page'
import { SectionTitle } from '../../components/SectionTitle'
import { useAsync } from '../../hooks/useAsync'

export const HomePage = () => {
  const navigate = useNavigate()
  const { data, loading, error } = useAsync(fetchInventorySummary, [], {
    initialValue: { activeCounts: 0, conflicts: 0 },
  })

  const hasContextInfos = useMemo(
    () => (data?.activeCounts ?? 0) > 0 || (data?.conflicts ?? 0) > 0,
    [data?.activeCounts, data?.conflicts],
  )

  return (
    <Page>
      <header className="flex flex-col gap-4">
        <p className="text-sm uppercase tracking-[0.3em] text-brand-200">CinéBoutique</p>
        <h1 className="text-4xl font-black leading-tight text-white sm:text-5xl">Inventaire simplifié</h1>
        <p className="max-w-xl text-base text-slate-300">
          Lancez un comptage en quelques gestes, scannez les produits depuis la caméra ou une douchette Bluetooth et
          assurez un suivi fiable de vos zones.
        </p>
      </header>

      <Card className="flex flex-col gap-4">
        <SectionTitle>État des inventaires</SectionTitle>
        {loading && <LoadingIndicator label="Chargement des indicateurs" />}
        {Boolean(error) && (
          <EmptyState title="Indisponible" description="Impossible de récupérer les informations pour l'instant." />
        )}
        {!loading && !error && data && hasContextInfos && (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="rounded-2xl border border-brand-500/30 bg-brand-500/10 p-5">
              <p className="text-sm uppercase text-brand-200">Comptages en cours</p>
              <p className="mt-2 text-4xl font-bold text-white">{data.activeCounts}</p>
            </div>
            <div className="rounded-2xl border border-red-500/40 bg-red-500/10 p-5">
              <p className="text-sm uppercase text-red-200">Conflits détectés</p>
              <p className="mt-2 text-4xl font-bold text-red-200">{data.conflicts}</p>
            </div>
          </div>
        )}
        {!loading && !error && data && !hasContextInfos && (
          <EmptyState
            title="Tout est calme"
            description="Aucun comptage en cours ni conflit détecté. Vous pouvez démarrer sereinement."
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
        <Link className="text-center text-sm text-slate-400 underline" to="/admin">
          Espace administrateur
        </Link>
      </div>
    </Page>
  )
}
