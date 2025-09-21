import { useCallback, useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { fetchLocations } from '../../api/inventoryApi'
import { Button } from '../../components/Button'
import { Card } from '../../components/Card'
import { EmptyState } from '../../components/EmptyState'
import { LoadingIndicator } from '../../components/LoadingIndicator'
import { TextField } from '../../components/TextField'
import { useInventory } from '../../contexts/InventoryContext'
import type { Location } from '../../types/inventory'
import { ApiError } from '../../api/client'

export const InventoryLocationStep = () => {
  const navigate = useNavigate()
  const { selectedUser, countType, location, setLocation } = useInventory()
  const [search, setSearch] = useState('')
  const [locations, setLocations] = useState<Location[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

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
    [],
  )

  useEffect(() => {
    if (!selectedUser) {
      navigate('/inventory/start', { replace: true })
    } else if (!countType) {
      navigate('/inventory/count-type', { replace: true })
    }
  }, [countType, navigate, selectedUser])

  useEffect(() => {
    let cancelled = false
    void loadLocations({ isCancelled: () => cancelled })
    return () => {
      cancelled = true
    }
  }, [loadLocations])

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
              const isSelected = location?.id === zone.id
              return (
                <button
                  key={zone.id}
                  type="button"
                  onClick={() => setLocation(zone)}
                  className={`flex flex-col gap-1 rounded-3xl border px-5 py-4 text-left transition-all ${
                    isSelected
                      ? 'border-brand-400 bg-brand-500/10 text-brand-700 dark:bg-brand-500/20 dark:text-brand-100'
                      : 'border-slate-200 bg-white text-slate-800 hover:border-brand-400/40 hover:bg-brand-50 dark:border-slate-700 dark:bg-slate-900/40 dark:text-slate-200'
                  }`}
                >
                  <span className="text-sm font-semibold uppercase tracking-wider text-brand-500">
                    {zone.code}
                  </span>
                  <span className="text-lg font-semibold">{zone.label}</span>
                  {zone.description && (
                    <span className="text-sm text-slate-500 dark:text-slate-400">{zone.description}</span>
                  )}
                </button>
              )
            })}
            {(Array.isArray(filteredLocations) ? filteredLocations : []).length === 0 && (
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
