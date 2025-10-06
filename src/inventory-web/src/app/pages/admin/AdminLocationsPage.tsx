import { useCallback, useMemo, useState } from 'react'
import { createLocation, updateLocation } from '../../api/adminApi'
import { fetchLocations } from '../../api/inventoryApi'
import { Button } from '../../components/ui/Button'
import { Input } from '../../components/ui/Input'
import { Card } from '../../components/Card'
import { EmptyState } from '../../components/EmptyState'
import { LoadingIndicator } from '../../components/LoadingIndicator'
import { useAsync } from '../../hooks/useAsync'
import type { Location } from '../../types/inventory'
import { useShop } from '@/state/ShopContext'

type FeedbackState = { type: 'success' | 'error'; message: string } | null

const formatBusyStatus = (location: Location) => {
  if (!location.isBusy) {
    return null
  }
  return location.busyBy ? `Occupée par ${location.busyBy}` : 'Occupée'
}

const LocationListItem = ({
  location,
  onSave,
}: {
  location: Location
  onSave: (id: string, payload: { code: string; label: string }) => Promise<void>
}) => {
  const [isEditing, setIsEditing] = useState(false)
  const [code, setCode] = useState(location.code)
  const [label, setLabel] = useState(location.label)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const resetForm = () => {
    setCode(location.code)
    setLabel(location.label)
    setError(null)
  }

  const handleCancel = () => {
    resetForm()
    setIsEditing(false)
  }

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setError(null)

    const nextCode = code.trim()
    const nextLabel = label.trim()

    if (!nextCode) {
      setError('Le code est requis.')
      return
    }

    if (!nextLabel) {
      setError('Le libellé est requis.')
      return
    }

    if (nextCode === location.code && nextLabel === location.label) {
      setIsEditing(false)
      return
    }

    setSaving(true)
    try {
      await onSave(location.id, { code: nextCode, label: nextLabel })
      setIsEditing(false)
    } catch (err) {
      const message = err instanceof Error ? err.message : 'La mise à jour a échoué.'
      setError(message)
    } finally {
      setSaving(false)
    }
  }

  const busyStatus = formatBusyStatus(location)

  return (
    <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm dark:border-slate-700 dark:bg-slate-900/70">
      {isEditing ? (
        <form className="flex flex-col gap-4" onSubmit={handleSubmit}>
          <div className="flex flex-col gap-4 sm:flex-row">
            <Input
              label="Code"
              name={`code-${location.id}`}
              value={code}
              onChange={(event) => setCode(event.target.value.toUpperCase())}
              containerClassName="sm:w-32"
              maxLength={12}
              autoComplete="off"
            />
            <Input
              label="Libellé"
              name={`label-${location.id}`}
              value={label}
              onChange={(event) => setLabel(event.target.value)}
              containerClassName="flex-1"
              autoComplete="off"
            />
          </div>
          {error && <p className="text-sm text-red-600 dark:text-red-400">{error}</p>}
          <div className="flex flex-col gap-2 sm:flex-row">
            <Button type="submit" className="py-3" disabled={saving}>
              {saving ? 'Enregistrement…' : 'Enregistrer'}
            </Button>
            <Button type="button" variant="ghost" onClick={handleCancel} disabled={saving}>
              Annuler
            </Button>
          </div>
        </form>
      ) : (
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <p className="text-sm font-semibold uppercase tracking-widest text-brand-500">{location.code}</p>
            <p className="text-lg font-semibold text-slate-900 dark:text-white">{location.label}</p>
            {busyStatus && <p className="text-sm text-slate-600 dark:text-slate-400">{busyStatus}</p>}
          </div>
          <Button variant="secondary" onClick={() => setIsEditing(true)}>
            Modifier
          </Button>
        </div>
      )}
    </div>
  )
}

export const AdminLocationsPage = () => {
  const { shop } = useShop()
  const loadLocations = useCallback(() => {
    if (!shop?.id) {
      return Promise.resolve<Location[]>([])
    }
    return fetchLocations(shop.id)
  }, [shop?.id])

  const { data, loading, error, execute, setData } = useAsync(loadLocations, [loadLocations], {
    initialValue: [],
    immediate: Boolean(shop?.id),
  })

  const [newLocationCode, setNewLocationCode] = useState('')
  const [newLocationLabel, setNewLocationLabel] = useState('')
  const [creatingLocation, setCreatingLocation] = useState(false)
  const [locationFeedback, setLocationFeedback] = useState<FeedbackState>(null)

  const sortedLocations = useMemo(
    () => [...(data ?? [])].sort((a, b) => a.code.localeCompare(b.code)),
    [data],
  )

  const handleCreateLocation = useCallback(
    async (event: React.FormEvent<HTMLFormElement>) => {
      event.preventDefault()
      setLocationFeedback(null)

      const code = newLocationCode.trim().toUpperCase()
      const label = newLocationLabel.trim()

      if (!code || !label) {
        setLocationFeedback({ type: 'error', message: 'Code et libellé sont requis.' })
        return
      }

      setCreatingLocation(true)
      try {
        const created = await createLocation({ code, label })
        setData((prev) => ([...(prev ?? []), created]))
        setNewLocationCode('')
        setNewLocationLabel('')
        setLocationFeedback({ type: 'success', message: 'Zone créée avec succès.' })
      } catch (err) {
        const message = err instanceof Error ? err.message : 'Impossible de créer la zone. Réessayez.'
        setLocationFeedback({ type: 'error', message })
      } finally {
        setCreatingLocation(false)
      }
    },
    [newLocationCode, newLocationLabel, setData],
  )

  const handleUpdateLocation = async (id: string, payload: { code: string; label: string }) => {
    setLocationFeedback(null)
    try {
      const updated = await updateLocation(id, payload)
      setData((prev) => prev?.map((item) => (item.id === updated.id ? updated : item)) ?? [])
      setLocationFeedback({ type: 'success', message: 'Zone mise à jour.' })
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Impossible de mettre à jour cette zone.'
      setLocationFeedback({ type: 'error', message })
      throw new Error(message)
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <Card className="flex flex-col gap-4">
        <div className="flex flex-col gap-1">
          <div className="flex items-center justify-between gap-4">
            <h2 className="text-xl font-semibold text-slate-900 dark:text-white">Zones</h2>
            <Button variant="ghost" onClick={() => execute()}>
              Actualiser
            </Button>
          </div>
          <p className="text-sm text-slate-600 dark:text-slate-400">
            Ajustez les codes visibles sur les étiquettes et leurs libellés.
          </p>
        </div>
        <form
          data-testid="location-create-form"
          className="flex flex-col gap-4 sm:flex-row"
          onSubmit={handleCreateLocation}
        >
          <Input
            label="Code"
            name="newLocationCode"
            placeholder="Ex. A01"
            value={newLocationCode}
            onChange={(event) => setNewLocationCode(event.target.value.toUpperCase())}
            containerClassName="sm:w-32"
            maxLength={12}
            autoComplete="off"
          />
          <Input
            label="Libellé"
            name="newLocationLabel"
            placeholder="Ex. Réserve, Comptoir"
            value={newLocationLabel}
            onChange={(event) => setNewLocationLabel(event.target.value)}
            containerClassName="flex-1"
            autoComplete="off"
          />
          <Button type="submit" disabled={creatingLocation} className="py-3">
            {creatingLocation ? 'Création…' : 'Ajouter'}
          </Button>
        </form>
        {locationFeedback && (
          <p
            className={`text-sm ${
              locationFeedback.type === 'success'
                ? 'text-emerald-600 dark:text-emerald-400'
                : 'text-red-600 dark:text-red-400'
            }`}
          >
            {locationFeedback.message}
          </p>
        )}
        {loading && <LoadingIndicator label="Chargement des zones" />}
        {Boolean(error) && <EmptyState title="Erreur" description="Les zones n'ont pas pu être chargées." />}
        {!loading && !error && (
          <div className="flex flex-col gap-3">
            {sortedLocations.length === 0 ? (
              <EmptyState title="Aucune zone" description="Ajoutez votre première zone pour démarrer." />
            ) : (
              sortedLocations.map((locationItem) => (
                <LocationListItem key={locationItem.id} location={locationItem} onSave={handleUpdateLocation} />
              ))
            )}
          </div>
        )}
      </Card>
    </div>
  )
}
