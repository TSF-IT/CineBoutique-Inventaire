import { useCallback, useState } from 'react'
import { createLocation, deleteLocation, updateLocation } from '../../api/adminApi'
import { fetchLocations } from '../../api/inventoryApi'
import { Button } from '../../components/ui/Button'
import { Input } from '../../components/ui/Input'
import { Card } from '../../components/Card'
import { EmptyState } from '../../components/EmptyState'
import { LoadingIndicator } from '../../components/LoadingIndicator'
import { SwipeActionItem } from '../../components/SwipeActionItem'
import { useAuth } from '../../contexts/AuthContext'
import { useAsync } from '../../hooks/useAsync'
import type { Location } from '../../types/inventory'

export const AdminLocationsPage = () => {
  const { user } = useAuth()
  const { data, loading, error, execute, setData } = useAsync(fetchLocations, [], { initialValue: [] })
  const [newLocationName, setNewLocationName] = useState('')
  const [creating, setCreating] = useState(false)
  const [feedback, setFeedback] = useState<string | null>(null)

  const handleCreate = useCallback(
    async (event: React.FormEvent<HTMLFormElement>) => {
      event.preventDefault()
      if (!newLocationName.trim()) {
        setFeedback('Saisissez un nom de zone avant de valider.')
        return
      }
      setCreating(true)
      setFeedback(null)
      try {
        const location = await createLocation({ label: newLocationName.trim() })
        setData((prev) => ([...(prev ?? []), location]))
        setNewLocationName('')
        setFeedback('Zone créée avec succès.')
      } catch {
        setFeedback('Impossible de créer la zone. Réessayez.')
      } finally {
        setCreating(false)
      }
    },
    [newLocationName, setData],
  )

  const handleRename = async (location: Location) => {
    const nextName = window.prompt('Nouveau libellé de zone', location.label)
    if (!nextName || nextName.trim() === location.label) {
      return
    }
    try {
      const updated = await updateLocation(location.id, { label: nextName.trim() })
      setData((prev) => prev?.map((item) => (item.id === updated.id ? updated : item)) ?? [])
      setFeedback('Zone renommée.')
    } catch {
      setFeedback('Impossible de renommer cette zone.')
    }
  }

  const handleDelete = async (location: Location) => {
    if (!window.confirm(`Supprimer la zone ${location.label} ?`)) {
      return
    }
    try {
      await deleteLocation(location.id)
      setData((prev) => prev?.filter((item) => item.id !== location.id) ?? [])
      setFeedback('Zone supprimée.')
    } catch {
      setFeedback('Suppression impossible. Retentez plus tard.')
    }
  }

  if (!user?.roles.includes('Admin')) {
    return <Card>Accès réservé aux administrateurs.</Card>
  }

  return (
    <div className="flex flex-col gap-6">
      <Card className="flex flex-col gap-4">
        <h2 className="text-xl font-semibold text-slate-900 dark:text-white">Ajouter une zone</h2>
        <form className="flex flex-col gap-4 sm:flex-row" onSubmit={handleCreate}>
          <Input
            label="Libellé"
            name="newLocationLabel"
            placeholder="Ex. Réserve, Comptoir"
            value={newLocationName}
            onChange={(event) => setNewLocationName(event.target.value)}
            containerClassName="flex-1"
          />
          <Button type="submit" disabled={creating} className="py-3">
            {creating ? 'Création…' : 'Ajouter'}
          </Button>
        </form>
        {feedback && <p className="text-sm text-slate-600 dark:text-slate-400">{feedback}</p>}
      </Card>

      <Card className="flex flex-col gap-4">
        <div className="flex items-center justify-between">
          <h2 className="text-xl font-semibold text-slate-900 dark:text-white">Zones existantes</h2>
          <Button variant="ghost" onClick={() => execute()}>
            Actualiser
          </Button>
        </div>
        {loading && <LoadingIndicator label="Chargement des zones" />}
        {Boolean(error) && <EmptyState title="Erreur" description="Les zones n&apos;ont pas pu être chargées." />}
        {!loading && !error && data && (
          <div className="flex flex-col gap-3">
            {data.map((locationItem) => (
              <SwipeActionItem
                key={locationItem.id}
                onEdit={() => handleRename(locationItem)}
                onDelete={() => handleDelete(locationItem)}
              >
                <div>
                  {locationItem.code && (
                    <p className="text-sm font-semibold uppercase tracking-widest text-brand-500">
                      {locationItem.code}
                    </p>
                  )}
                  <p className="text-lg font-semibold text-slate-900 dark:text-white">{locationItem.label}</p>
                  <p className="text-sm text-slate-600 dark:text-slate-400">
                    {locationItem.isBusy
                      ? `Occupée${locationItem.busyBy ? ` par ${locationItem.busyBy}` : ''}`
                      : 'Libre'}
                  </p>
                </div>
              </SwipeActionItem>
            ))}
            {data.length === 0 && (
              <EmptyState title="Aucune zone" description="Ajoutez votre première zone pour démarrer." />
            )}
          </div>
        )}
      </Card>
    </div>
  )
}
