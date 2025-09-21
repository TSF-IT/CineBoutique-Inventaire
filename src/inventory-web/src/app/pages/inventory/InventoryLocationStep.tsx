import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { fetchLocations } from '../../api/inventoryApi'
import { Button } from '../../components/Button'
import { Card } from '../../components/Card'
import { EmptyState } from '../../components/EmptyState'
import { LoadingIndicator } from '../../components/LoadingIndicator'
import { TextField } from '../../components/TextField'
import { useInventory } from '../../contexts/InventoryContext'
import { useAsync } from '../../hooks/useAsync'

export const InventoryLocationStep = () => {
  const navigate = useNavigate()
  const { selectedUser, countType, location, setLocation } = useInventory()
  const [search, setSearch] = useState('')
  const { data, loading, error, execute } = useAsync(fetchLocations, [], {})

  useEffect(() => {
    if (!selectedUser) {
      navigate('/inventory/start', { replace: true })
    } else if (!countType) {
      navigate('/inventory/count-type', { replace: true })
    }
  }, [countType, navigate, selectedUser])

  const filteredLocations = useMemo(() => {
    if (!data) return []
    if (!search.trim()) return data
    return data.filter((zone) => zone.name.toLowerCase().includes(search.toLowerCase()))
  }, [data, search])

  return (
    <div className="flex flex-col gap-6">
      <Card className="flex flex-col gap-4">
        <div className="flex items-center justify-between gap-3">
          <div>
            <h2 className="text-2xl font-semibold text-white">Sélectionnez la zone</h2>
            <p className="text-sm text-slate-400">Choisissez la zone physique ou logique à inventorier.</p>
          </div>
          <Button variant="ghost" onClick={() => execute()}>
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
        {Boolean(error) && (
          <EmptyState
            title="Impossible de charger les zones"
            description="Vérifiez votre connexion ou réessayez plus tard."
          />
        )}
        {!loading && !error && (
          <div className="flex flex-col gap-3">
            {filteredLocations.map((zone) => {
              const isSelected = location?.id === zone.id
              return (
                <button
                  key={zone.id}
                  type="button"
                  onClick={() => setLocation(zone)}
                  className={`flex flex-col gap-1 rounded-3xl border px-5 py-4 text-left transition-all ${
                    isSelected
                      ? 'border-brand-400 bg-brand-500/20 text-brand-100'
                      : 'border-slate-700 bg-slate-900/40 text-slate-200 hover:border-brand-400/30'
                  }`}
                >
                  <span className="text-lg font-semibold">{zone.name}</span>
                  {zone.description && <span className="text-sm text-slate-400">{zone.description}</span>}
                </button>
              )
            })}
            {filteredLocations.length === 0 && (
              <EmptyState title="Aucune zone" description="Aucune zone ne correspond à votre recherche." />
            )}
          </div>
        )}
      </Card>
      {location && (
        <Button fullWidth className="py-4" onClick={() => navigate('/inventory/confirm')}>
          Vérifier la disponibilité
        </Button>
      )}
    </div>
  )
}
