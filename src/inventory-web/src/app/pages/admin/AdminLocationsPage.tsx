import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import clsx from 'clsx'
import {
  createLocation,
  updateLocation,
  createShopUser,
  updateShopUser,
  disableShopUser,
} from '../../api/adminApi'
import { fetchLocations } from '../../api/inventoryApi'
import { fetchShopUsers } from '../../api/shopUsers'
import { Button } from '../../components/ui/Button'
import { Input } from '../../components/ui/Input'
import { FileUploadField } from '../../components/ui/FileUploadField'
import { Card } from '../../components/Card'
import { EmptyState } from '../../components/EmptyState'
import { LoadingIndicator } from '../../components/LoadingIndicator'
import { useAsync } from '../../hooks/useAsync'
import type { Location } from '../../types/inventory'
import type { ShopUser } from '@/types/user'
import { useShop } from '@/state/ShopContext'

type FeedbackState = { type: 'success' | 'error'; message: string } | null
type AdminSection = 'locations' | 'users' | 'catalog'

const ADMIN_SECTIONS: { id: AdminSection; label: string; description: string }[] = [
  {
    id: 'locations',
    label: 'Zones',
    description: 'Ajustez les codes visibles sur les étiquettes et leurs libellés associés.',
  },
  {
    id: 'users',
    label: 'Utilisateurs',
    description: 'Créez, mettez à jour ou désactivez les comptes des personnes autorisées à inventorier.',
  },
  {
    id: 'catalog',
    label: 'Produits',
    description: 'Importez ou simulez un import CSV pour mettre à jour le catalogue de la boutique.',
  },
]

const formatBusyStatus = (location: Location) => {
  if (!location.isBusy) {
    return null
  }
  return location.busyBy ? `Occupée par ${location.busyBy}` : 'Occupée'
}

type SectionSwitcherProps = {
  activeSection: AdminSection
  onChange: (section: AdminSection) => void
}

const SectionSwitcher = ({ activeSection, onChange }: SectionSwitcherProps) => (
  <Card className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
    <div>
      <h2 className="text-xl font-semibold text-slate-900 dark:text-white">Paramétrage rapide</h2>
      <p className="text-sm text-slate-600 dark:text-slate-400">
        Choisissez la rubrique à modifier. Les actions sont pensées pour un usage tactile ou souris.
      </p>
    </div>
    <div className="flex justify-start sm:justify-end">
      <div
        role="tablist"
        aria-label="Choix de la section d'administration"
        className="inline-grid w-full min-w-[200px] grid-flow-col auto-cols-fr gap-1 rounded-full bg-slate-100 p-0.5 text-sm font-semibold text-slate-600 shadow-inner dark:bg-slate-800 dark:text-slate-300 sm:w-auto"
      >
        {ADMIN_SECTIONS.map(({ id, label }) => {
          const isActive = id === activeSection
          return (
            <button
              key={id}
              type="button"
              role="tab"
              aria-selected={isActive}
              className={clsx(
                'rounded-full px-3 py-1.5 transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-400',
                isActive
                  ? 'bg-white text-slate-900 shadow-sm dark:bg-slate-700 dark:text-white'
                  : 'hover:text-slate-900 dark:hover:text-white',
              )}
              onClick={() => onChange(id)}
            >
              {label}
            </button>
          )
        })}
      </div>
    </div>
  </Card>
)

type ImportSummary = {
  total: number
  inserted: number
  updated: number
  errorCount: number
  unknownColumns: string[]
}

type CatalogImportFeedback =
  | { type: 'success'; summary: ImportSummary }
  | { type: 'info'; message: string }
  | { type: 'error'; message: string; details?: string[] }

const CatalogImportPanel = ({ description }: { description: string }) => {
  const { shop } = useShop()
  const [file, setFile] = useState<File | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [feedback, setFeedback] = useState<CatalogImportFeedback | null>(null)
  const fileInputRef = useRef<HTMLInputElement | null>(null)

  const handleFileChange = (nextFile: File | null) => {
    setFile(nextFile)
    setFeedback(null)
  }

  const resetFileInput = () => {
    if (fileInputRef.current) {
      fileInputRef.current.value = ''
    }
    setFile(null)
  }

  const toInteger = (value: unknown) => {
    if (typeof value === 'number' && Number.isFinite(value)) {
      return Math.trunc(value)
    }
    if (typeof value === 'string') {
      const parsed = Number.parseInt(value, 10)
      return Number.isNaN(parsed) ? 0 : parsed
    }
    return 0
  }

  const toStringList = (value: unknown) => {
    if (!Array.isArray(value)) {
      return []
    }
    return value
      .map((item) => (typeof item === 'string' ? item.trim() : ''))
      .filter((item): item is string => item.length > 0)
  }

  const parseJson = (text: string) => {
    try {
      return JSON.parse(text) as Record<string, unknown>
    } catch {
      return null
    }
  }

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setFeedback(null)

    if (!shop?.id) {
      setFeedback({ type: 'error', message: 'Boutique introuvable. Veuillez recharger la page.' })
      return
    }

    if (!file) {
      setFeedback({ type: 'error', message: "Sélectionnez un fichier CSV avant de lancer l'import." })
      return
    }

    setSubmitting(true)
    try {
      const fd = new FormData()
      fd.set('file', file)

      const url = `/api/shops/${shop.id}/products/import?dryRun=false`
      const response = await fetch(url, { method: 'POST', body: fd })
      const rawText = await response.text()
      const payload = rawText ? parseJson(rawText) : null
      const record = (payload ?? {}) as Record<string, unknown>

      if (response.status === 200) {
        const summary: ImportSummary = {
          total: toInteger(record.total),
          inserted: toInteger(record.inserted),
          updated: toInteger(record.updated),
          errorCount: toInteger(record.errorCount),
          unknownColumns: toStringList(record.unknownColumns),
        }
        setFeedback({ type: 'success', summary })
        resetFileInput()
        return
      }

      if (response.status === 204) {
        setFeedback({ type: 'info', message: 'Aucun changement (fichier déjà importé).' })
        resetFileInput()
        return
      }

      if (response.status === 423) {
        setFeedback({ type: 'error', message: 'Un import est déjà en cours.' })
        return
      }

      if (response.status === 413) {
        setFeedback({ type: 'error', message: 'Fichier trop volumineux (25 MiB max).' })
        return
      }

      if (response.status === 400) {
        const aggregatedDetails = [
          ...toStringList(record.errors),
          ...toStringList(record.errorMessages),
          ...toStringList(record.details),
        ]
        const uniqueDetails = Array.from(new Set(aggregatedDetails))
        const message =
          typeof record.message === 'string'
            ? record.message
            : typeof record.error === 'string'
              ? record.error
              : rawText && rawText.trim().length > 0
                ? rawText
                : 'Le fichier CSV est invalide.'
        setFeedback({
          type: 'error',
          message,
          details: uniqueDetails.length > 0 ? uniqueDetails : undefined,
        })
        return
      }

      const fallbackMessage =
        (typeof record.message === 'string' && record.message) ||
        (typeof record.error === 'string' && record.error) ||
        (rawText && rawText.trim().length > 0 ? rawText : `Erreur inattendue (${response.status}).`)
      setFeedback({ type: 'error', message: fallbackMessage })
    } catch (error) {
      setFeedback({
        type: 'error',
        message: "L'import a échoué. Vérifiez votre connexion et réessayez.",
      })
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="flex flex-col gap-4 rounded-lg border bg-white shadow-sm p-4">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h2 className="text-xl font-semibold text-slate-900 dark:text-white">Catalogue produits (CSV)</h2>
          <p className="text-sm text-slate-600 dark:text-slate-400">{description}</p>
        </div>
      </div>
      <form className="flex flex-col gap-4" onSubmit={handleSubmit} encType="multipart/form-data">
        <FileUploadField
          ref={fileInputRef}
          name="file"
          label="Fichier CSV"
          accept=".csv,text/csv"
          file={file}
          onFileSelected={handleFileChange}
          disabled={submitting}
          description="Glissez-déposez votre fichier ou cliquez pour parcourir vos dossiers."
        />
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-end">
          <Button
            type="submit"
            disabled={submitting || !file}
            className="w-full sm:w-auto"
          >
            {submitting ? 'Import en cours…' : 'Importer le CSV'}
          </Button>
        </div>
      </form>
      {feedback && (
        <div
          role={feedback.type === 'error' ? 'alert' : 'status'}
          className={clsx(
            'rounded-lg border p-4 text-sm',
            feedback.type === 'success' && 'border-emerald-200 bg-emerald-50 text-emerald-800',
            feedback.type === 'info' && 'border-slate-200 bg-slate-50 text-slate-700',
            feedback.type === 'error' && 'border-red-200 bg-red-50 text-red-700',
          )}
        >
          {feedback.type === 'success' ? (
            <div className="space-y-3">
              <p className="font-medium">
                Import terminé avec succès.
              </p>
              <dl className="grid grid-cols-1 gap-2 text-sm sm:grid-cols-2">
                <div>
                  <dt className="font-medium text-slate-700">Total</dt>
                  <dd className="text-slate-600">{feedback.summary.total}</dd>
                </div>
                <div>
                  <dt className="font-medium text-slate-700">Insérés</dt>
                  <dd className="text-slate-600">{feedback.summary.inserted}</dd>
                </div>
                <div>
                  <dt className="font-medium text-slate-700">Mis à jour</dt>
                  <dd className="text-slate-600">{feedback.summary.updated}</dd>
                </div>
                <div>
                  <dt className="font-medium text-slate-700">Erreurs détectées</dt>
                  <dd className="text-slate-600">{feedback.summary.errorCount}</dd>
                </div>
              </dl>
              {feedback.summary.unknownColumns.length > 0 && (
                <div className="space-y-2">
                  <p className="font-medium text-slate-700">Colonnes inconnues</p>
                  <ul className="list-disc space-y-1 pl-5 text-sm text-slate-600">
                    {feedback.summary.unknownColumns.map((column) => (
                      <li key={column} className="max-w-full truncate" title={column}>
                        {column}
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </div>
          ) : feedback.type === 'info' ? (
            <p className="font-medium">{feedback.message}</p>
          ) : (
            <div className="space-y-2">
              <p className="font-medium">{feedback.message}</p>
              {feedback.details && feedback.details.length > 0 && (
                <ul className="list-disc space-y-1 pl-5 text-sm">
                  {feedback.details.map((detail) => (
                    <li key={detail} className="max-w-full break-words">
                      {detail}
                    </li>
                  ))}
                </ul>
              )}
            </div>
          )}
        </div>
      )}
    </div>
  )
}

type LocationListItemProps = {
  location: Location
  onSave: (id: string, payload: { code: string; label: string }) => Promise<void>
}

const LocationListItem = ({ location, onSave }: LocationListItemProps) => {
  const [isEditing, setIsEditing] = useState(false)
  const [code, setCode] = useState(location.code)
  const [label, setLabel] = useState(location.label)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!isEditing) {
      setCode(location.code)
      setLabel(location.label)
      setError(null)
    }
  }, [isEditing, location.code, location.label])

  const handleCancel = () => {
    setCode(location.code)
    setLabel(location.label)
    setError(null)
    setIsEditing(false)
  }

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setError(null)

    const nextCode = code.trim().toUpperCase()
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
    <div className="flex h-full flex-col rounded-2xl border border-slate-200 bg-white p-4 shadow-sm dark:border-slate-700 dark:bg-slate-900/70">
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
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between sm:gap-6">
          <div className="min-w-0 space-y-1">
            <p className="text-sm font-semibold uppercase tracking-widest text-brand-500">{location.code}</p>
            <p className="text-lg font-semibold text-slate-900 dark:text-white break-words">{location.label}</p>
            {busyStatus && <p className="text-sm text-slate-600 dark:text-slate-400 break-words">{busyStatus}</p>}
          </div>
          <Button variant="secondary" onClick={() => setIsEditing(true)} className="sm:self-start">
            Modifier
          </Button>
        </div>
      )}
    </div>
  )
}

type UserListItemProps = {
  user: ShopUser
  onSave: (id: string, payload: { login: string; displayName: string; isAdmin: boolean }) => Promise<void>
  onDisable: (id: string) => Promise<void>
}

const UserListItem = ({ user, onSave, onDisable }: UserListItemProps) => {
  const [isEditing, setIsEditing] = useState(false)
  const [login, setLogin] = useState(user.login)
  const [displayName, setDisplayName] = useState(user.displayName)
  const [isAdmin, setIsAdmin] = useState(user.isAdmin)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const disableConfirmationDialogRef = useRef<HTMLDialogElement | null>(null)
  const disableConfirmButtonRef = useRef<HTMLButtonElement | null>(null)
  const disableDialogTitleId = `disable-user-dialog-title-${user.id}`
  const disableDialogDescriptionId = `disable-user-dialog-description-${user.id}`
  const disableConfirmationMessage = `Désactiver ${user.displayName} ? L'utilisateur ne pourra plus se connecter tant qu'il n'est pas recréé.`

  useEffect(() => {
    if (!isEditing) {
      setLogin(user.login)
      setDisplayName(user.displayName)
      setIsAdmin(user.isAdmin)
      setError(null)
    }
  }, [isEditing, user.displayName, user.isAdmin, user.login])

  const handleCancel = () => {
    setLogin(user.login)
    setDisplayName(user.displayName)
    setIsAdmin(user.isAdmin)
    setError(null)
    setIsEditing(false)
  }

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setError(null)

    const nextLogin = login.trim()
    const nextDisplayName = displayName.trim()

    if (!nextLogin) {
      setError("L'identifiant est requis.")
      return
    }

    if (!nextDisplayName) {
      setError('Le nom affiché est requis.')
      return
    }

    if (nextLogin === user.login && nextDisplayName === user.displayName && isAdmin === user.isAdmin) {
      setIsEditing(false)
      return
    }

    setSaving(true)
    try {
      await onSave(user.id, { login: nextLogin, displayName: nextDisplayName, isAdmin })
      setIsEditing(false)
    } catch (err) {
      const message = err instanceof Error ? err.message : 'La mise à jour a échoué.'
      setError(message)
    } finally {
      setSaving(false)
    }
  }

  const performDisable = useCallback(async () => {
    setError(null)
    setSaving(true)
    try {
      await onDisable(user.id)
    } catch (err) {
      const message = err instanceof Error ? err.message : 'La désactivation a échoué.'
      setError(message)
    } finally {
      setSaving(false)
    }
  }, [onDisable, user.id])

  const handleOpenDisableDialog = useCallback(() => {
    const dialog = disableConfirmationDialogRef.current
    if (dialog && typeof dialog.showModal === 'function') {
      dialog.showModal()
      requestAnimationFrame(() => {
        disableConfirmButtonRef.current?.focus()
      })
      return
    }

    if (window.confirm(disableConfirmationMessage)) {
      void performDisable()
    }
  }, [disableConfirmationMessage, performDisable])

  const handleCancelDisableDialog = useCallback(() => {
    disableConfirmationDialogRef.current?.close()
  }, [])

  const handleConfirmDisableDialog = useCallback(() => {
    disableConfirmationDialogRef.current?.close()
    void performDisable()
  }, [performDisable])

  return (
    <div
      className="flex h-full flex-col rounded-2xl border border-slate-200 bg-white p-4 shadow-sm dark:border-slate-700 dark:bg-slate-900/70"
      data-testid="user-card"
      data-user-id={user.id}
    >
      {isEditing ? (
        <form className="flex flex-col gap-4" onSubmit={handleSubmit}>
          <div className="flex flex-col gap-4 lg:flex-row">
            <Input
              label="Identifiant"
              name={`login-${user.id}`}
              value={login}
              onChange={(event) => setLogin(event.target.value)}
              containerClassName="lg:w-48"
              maxLength={64}
              autoCapitalize="none"
              autoComplete="off"
              autoCorrect="off"
              spellCheck={false}
            />
            <Input
              label="Nom affiché"
              name={`displayName-${user.id}`}
              value={displayName}
              onChange={(event) => setDisplayName(event.target.value)}
              containerClassName="flex-1"
              autoComplete="name"
            />
          </div>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
            <label className="flex items-center gap-3 text-sm font-medium text-slate-700 dark:text-slate-200">
              <input
                type="checkbox"
                checked={isAdmin}
                onChange={(event) => setIsAdmin(event.target.checked)}
                className="h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-500 dark:border-slate-600"
              />
              Administrateur
            </label>
            <div className="flex flex-col gap-2 sm:flex-row">
              <Button type="submit" className="py-3" disabled={saving}>
                {saving ? 'Enregistrement…' : 'Enregistrer'}
              </Button>
              <Button type="button" variant="ghost" onClick={handleCancel} disabled={saving}>
                Annuler
              </Button>
            </div>
          </div>
          {error && <p className="text-sm text-red-600 dark:text-red-400">{error}</p>}
        </form>
      ) : (
        <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between sm:gap-6">
          <div className="min-w-0 space-y-1">
            <p className="text-sm font-semibold uppercase tracking-widest text-brand-500 break-words">{user.login}</p>
            <p className="text-lg font-semibold text-slate-900 dark:text-white break-words">{user.displayName}</p>
            <div className="flex flex-wrap gap-2">
              <span className="inline-flex items-center rounded-full bg-slate-100 px-3 py-1 text-xs font-medium text-slate-700 dark:bg-slate-800 dark:text-slate-200">
                {user.isAdmin ? 'Administrateur' : 'Standard'}
              </span>
            </div>
          </div>
          <div className="flex flex-col gap-2 sm:flex-row sm:flex-none sm:items-center">
            <Button variant="secondary" onClick={() => setIsEditing(true)}>
              Modifier
            </Button>
            <Button
              variant="ghost"
              className="text-red-600 hover:text-red-700 dark:text-red-400 dark:hover:text-red-300"
              onClick={handleOpenDisableDialog}
              disabled={saving}
            >
              Désactiver
            </Button>
          </div>
          {error && <p className="text-sm text-red-600 dark:text-red-400 sm:ml-auto sm:w-full sm:text-right">{error}</p>}
        </div>
      )}
      <dialog
        ref={disableConfirmationDialogRef}
        aria-modal="true"
        aria-labelledby={disableDialogTitleId}
        aria-describedby={disableDialogDescriptionId}
        className="px-4"
      >
        <Card className="w-full max-w-lg shadow-elev-2">
          <div className="space-y-4">
            <p id={disableDialogTitleId} className="text-lg font-semibold">
              {`Désactiver ${user.displayName} ?`}
            </p>
            <p id={disableDialogDescriptionId} className="text-sm text-slate-600 dark:text-slate-300">
              L'utilisateur ne pourra plus se connecter tant qu'il n'est pas recréé.
            </p>
          </div>
          <div className="mt-6 flex justify-end gap-3">
            <Button type="button" variant="secondary" onClick={handleCancelDisableDialog}>
              Annuler
            </Button>
            <Button
              ref={disableConfirmButtonRef}
              type="button"
              onClick={handleConfirmDisableDialog}
              className="bg-red-600 text-white shadow-soft hover:bg-red-500 focus-visible:ring-2 focus-visible:ring-red-300 dark:bg-red-500 dark:hover:bg-red-400"
              disabled={saving}
            >
              Confirmer la désactivation
            </Button>
          </div>
        </Card>
      </dialog>
    </div>
  )
}

type LocationsPanelProps = {
  description: string
}

const LocationsPanel = ({ description }: LocationsPanelProps) => {
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
    <Card className="flex flex-col gap-4">
      <div className="flex flex-col gap-1">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <h2 className="text-xl font-semibold text-slate-900 dark:text-white">Zones</h2>
            <p className="text-sm text-slate-600 dark:text-slate-400">{description}</p>
          </div>
          <Button variant="ghost" onClick={() => execute()}>
            Actualiser
          </Button>
        </div>
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
        <div className="grid grid-cols-1 gap-4">
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
  )
}

type UsersPanelProps = {
  description: string
  isActive: boolean
}

const UsersPanel = ({ description, isActive }: UsersPanelProps) => {
  const { shop } = useShop()

  const loadUsers = useCallback(() => {
    if (!shop?.id) {
      return Promise.resolve<ShopUser[]>([])
    }
    return fetchShopUsers(shop.id)
  }, [shop?.id])

  const { data, loading, error, execute, setData } = useAsync(loadUsers, [loadUsers], {
    initialValue: [],
    immediate: false,
  })

  const [hasRequested, setHasRequested] = useState(false)
  const [newUserLogin, setNewUserLogin] = useState('')
  const [newUserDisplayName, setNewUserDisplayName] = useState('')
  const [newUserIsAdmin, setNewUserIsAdmin] = useState(false)
  const [creatingUser, setCreatingUser] = useState(false)
  const [userFeedback, setUserFeedback] = useState<FeedbackState>(null)

  useEffect(() => {
    setHasRequested(false)
    setData([])
  }, [setData, shop?.id])

  useEffect(() => {
    if (!isActive) {
      return
    }
    if (!shop?.id) {
      return
    }
    if (hasRequested) {
      return
    }
    setHasRequested(true)
    void execute().catch(() => undefined)
  }, [execute, hasRequested, isActive, shop?.id])

  const sortedUsers = useMemo(() => {
    const list = data ?? []
    return [...list].sort((a, b) => a.displayName.localeCompare(b.displayName, 'fr', { sensitivity: 'base' }))
  }, [data])

  const handleCreateUser = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setUserFeedback(null)

    const login = newUserLogin.trim()
    const displayName = newUserDisplayName.trim()

    if (!shop?.id) {
      setUserFeedback({ type: 'error', message: 'Sélectionnez une boutique avant de créer un utilisateur.' })
      return
    }

    if (!login || !displayName) {
      setUserFeedback({ type: 'error', message: 'Identifiant et nom affiché sont requis.' })
      return
    }

    setCreatingUser(true)
    try {
      const created = await createShopUser(shop.id, { login, displayName, isAdmin: newUserIsAdmin })
      setData((prev) => ([...(prev ?? []), created]))
      setNewUserLogin('')
      setNewUserDisplayName('')
      setNewUserIsAdmin(false)
      setUserFeedback({ type: 'success', message: 'Utilisateur créé avec succès.' })
    } catch (err) {
      const message = err instanceof Error ? err.message : "Impossible de créer l'utilisateur. Réessayez."
      setUserFeedback({ type: 'error', message })
    } finally {
      setCreatingUser(false)
    }
  }

  const handleUpdateUser = async (
    id: string,
    payload: { login: string; displayName: string; isAdmin: boolean },
  ): Promise<void> => {
    setUserFeedback(null)
    if (!shop?.id) {
      const message = 'Boutique introuvable. Veuillez recharger la page.'
      setUserFeedback({ type: 'error', message })
      throw new Error(message)
    }

    try {
      const updated = await updateShopUser(shop.id, { id, ...payload })
      setData((prev) => prev?.map((item) => (item.id === updated.id ? updated : item)) ?? [])
      setUserFeedback({ type: 'success', message: 'Utilisateur mis à jour.' })
    } catch (err) {
      const message = err instanceof Error ? err.message : "Impossible de mettre à jour l'utilisateur." 
      setUserFeedback({ type: 'error', message })
      throw new Error(message)
    }
  }

  const handleDisableUser = async (id: string): Promise<void> => {
    setUserFeedback(null)
    if (!shop?.id) {
      const message = 'Boutique introuvable. Veuillez recharger la page.'
      setUserFeedback({ type: 'error', message })
      throw new Error(message)
    }

    try {
      await disableShopUser(shop.id, id)
      setData((prev) => prev?.filter((item) => item.id !== id) ?? [])
      setUserFeedback({ type: 'success', message: 'Utilisateur désactivé.' })
    } catch (err) {
      const message = err instanceof Error ? err.message : "Impossible de désactiver l'utilisateur." 
      setUserFeedback({ type: 'error', message })
      throw new Error(message)
    }
  }

  const handleRefresh = () => {
    if (!shop?.id) {
      setUserFeedback({ type: 'error', message: 'Boutique introuvable. Impossible de rafraîchir.' })
      return
    }
    setUserFeedback(null)
    setHasRequested(true)
    void execute().catch(() => undefined)
  }

  return (
    <Card className="flex flex-col gap-4">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h2 className="text-xl font-semibold text-slate-900 dark:text-white">Utilisateurs</h2>
          <p className="text-sm text-slate-600 dark:text-slate-400">{description}</p>
        </div>
        <Button variant="ghost" onClick={handleRefresh}>
          Actualiser
        </Button>
      </div>
      <form className="flex flex-col gap-4" data-testid="user-create-form" onSubmit={handleCreateUser}>
        <div className="flex flex-col gap-4 lg:flex-row lg:items-end">
          <div className="flex flex-col gap-4 sm:flex-row sm:flex-wrap">
            <Input
              label="Identifiant"
              name="newUserLogin"
              placeholder="Ex. camille"
              value={newUserLogin}
              onChange={(event) => setNewUserLogin(event.target.value)}
              containerClassName="sm:w-48"
              maxLength={64}
              autoCapitalize="none"
              autoComplete="off"
              autoCorrect="off"
              spellCheck={false}
            />
            <Input
              label="Nom affiché"
              name="newUserDisplayName"
              placeholder="Ex. Camille Dupont"
              value={newUserDisplayName}
              onChange={(event) => setNewUserDisplayName(event.target.value)}
              containerClassName="flex-1"
              autoComplete="name"
            />
          </div>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
            <label className="flex items-center gap-3 text-sm font-medium text-slate-700 dark:text-slate-200">
              <input
                type="checkbox"
                checked={newUserIsAdmin}
                onChange={(event) => setNewUserIsAdmin(event.target.checked)}
                className="h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-500 dark:border-slate-600"
              />
              Administrateur
            </label>
            <Button type="submit" disabled={creatingUser} className="py-3">
              {creatingUser ? 'Création…' : 'Ajouter'}
            </Button>
          </div>
        </div>
      </form>
      {userFeedback && (
        <p
          className={`text-sm ${
            userFeedback.type === 'success'
              ? 'text-emerald-600 dark:text-emerald-400'
              : 'text-red-600 dark:text-red-400'
          }`}
        >
          {userFeedback.message}
        </p>
      )}
      {loading && <LoadingIndicator label="Chargement des utilisateurs" />}
      {Boolean(error) && hasRequested && (
        <EmptyState title="Erreur" description="Les utilisateurs n'ont pas pu être chargés." />
      )}
      {!loading && !error && hasRequested && (
        <div className="grid grid-cols-1 gap-4">
          {sortedUsers.length === 0 ? (
            <EmptyState title="Aucun utilisateur" description="Ajoutez un premier compte pour démarrer." />
          ) : (
            sortedUsers.map((user) => (
              <UserListItem key={user.id} user={user} onSave={handleUpdateUser} onDisable={handleDisableUser} />
            ))
          )}
        </div>
      )}
    </Card>
  )
}

export const AdminLocationsPage = () => {
  const [activeSection, setActiveSection] = useState<AdminSection>('locations')
  const activeDefinition = ADMIN_SECTIONS.find((section) => section.id === activeSection) ?? ADMIN_SECTIONS[0]

  return (
    <div className="flex flex-col gap-6">
      <SectionSwitcher activeSection={activeSection} onChange={setActiveSection} />
      {activeSection === 'locations' ? (
        <LocationsPanel description={activeDefinition.description} />
      ) : activeSection === 'users' ? (
        <UsersPanel description={activeDefinition.description} isActive={activeSection === 'users'} />
      ) : (
        <CatalogImportPanel description={activeDefinition.description} />
      )}
    </div>
  )
}
