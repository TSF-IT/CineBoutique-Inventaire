import type { MouseEvent } from 'react'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import clsx from 'clsx'
import { getCompletedRunDetail } from '../../api/inventoryApi'
import type { CompletedRunDetail, CompletedRunSummary } from '../../types/inventory'
import type { HttpError } from '@/lib/api/http'
import { Button } from '../ui/Button'
import { ErrorPanel } from '../ErrorPanel'
import { LoadingIndicator } from '../LoadingIndicator'

interface CompletedRunsModalProps {
  open: boolean
  completedRuns: CompletedRunSummary[]
  onClose: () => void
}

const focusableSelectors = [
  'a[href]',
  'button:not([disabled])',
  'textarea:not([disabled])',
  'input:not([disabled])',
  'select:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
].join(', ')

const describeCountType = (value: number) => {
  switch (value) {
    case 1:
      return 'Comptage n°1'
    case 2:
      return 'Comptage n°2'
    case 3:
      return 'Comptage n°3'
    default:
      return `Comptage ${value}`
  }
}

const formatDateTime = (value: string | null | undefined) => {
  if (!value) {
    return 'Non disponible'
  }
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return 'Non disponible'
  }
  return new Intl.DateTimeFormat('fr-FR', {
    dateStyle: 'short',
    timeStyle: 'short',
  }).format(date)
}

const toValidOwnerName = (name: string | null | undefined) => {
  const trimmed = name?.trim()
  return trimmed && trimmed.length > 0 ? trimmed : null
}

const formatOwnerName = (name: string | null | undefined) => toValidOwnerName(name) ?? '—'

const formatOwnerForTitle = (run: CompletedRunSummary, detail?: CompletedRunDetail | null) =>
  toValidOwnerName(detail?.ownerDisplayName ?? run.ownerDisplayName) ?? 'Inconnu'

const formatDateOnly = (value: string | null | undefined) => {
  if (!value) {
    return 'Non disponible'
  }
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return 'Non disponible'
  }
  return new Intl.DateTimeFormat('fr-FR', { dateStyle: 'short' }).format(date)
}

const formatDateTimeLong = (value: string | null | undefined) => {
  if (!value) {
    return 'Non disponible'
  }
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return 'Non disponible'
  }
  return new Intl.DateTimeFormat('fr-FR', {
    dateStyle: 'long',
    timeStyle: 'short',
  }).format(date)
}

const formatQuantity = (value: number) =>
  new Intl.NumberFormat('fr-FR', {
    maximumFractionDigits: 3,
    minimumFractionDigits: 0,
  }).format(value)

const buildRunTitle = (run: CompletedRunSummary, detail?: CompletedRunDetail | null) => {
  const owner = formatOwnerForTitle(run, detail)
  const completedOn = formatDateOnly(detail?.completedAtUtc ?? run.completedAtUtc)
  return `${describeCountType(run.countType)} de la zone ${run.locationCode} par ${owner} le ${completedOn}`
}

const escapeCsvValue = (value: string) => `"${value.replace(/"/g, '""')}"`

const buildCsvContent = (title: string, detail: CompletedRunDetail) => {
  const header = ['EAN', 'Libellé', 'Quantité']
  const lines = detail.items.map((item) =>
    [
      escapeCsvValue(item.ean ?? ''),
      escapeCsvValue(item.name),
      escapeCsvValue(formatQuantity(item.quantity)),
    ].join(';'),
  )

  return [escapeCsvValue(title), header.map(escapeCsvValue).join(';'), ...lines].join('\n')
}

const slugify = (value: string) =>
  value
    .normalize('NFD')
    .replace(/[^\p{Letter}\p{Number}]+/gu, '-')
    .replace(/^-+|-+$/g, '')
    .toLowerCase() || 'export'

const describeDetailError = (
  error: unknown,
): { title: string; details?: string } | null => {
  if (!error) {
    return null
  }
  const isHttp = (candidate: unknown): candidate is HttpError =>
    typeof candidate === 'object' &&
    candidate !== null &&
    typeof (candidate as { status?: number }).status === 'number' &&
    typeof (candidate as { url?: string }).url === 'string'

  if (isHttp(error)) {
    const message =
      (error.problem as { detail?: string } | undefined)?.detail ||
      (error.problem as { title?: string } | undefined)?.title ||
      error.body ||
      `HTTP ${error.status}`
    return { title: 'Erreur de chargement', details: message }
  }
  if (error instanceof Error) {
    return { title: 'Erreur', details: error.message }
  }
  if (typeof error === 'string') {
    return { title: 'Erreur', details: error }
  }
  return { title: 'Erreur', details: 'Une erreur inattendue est survenue.' }
}

export const CompletedRunsModal = ({ open, completedRuns, onClose }: CompletedRunsModalProps) => {
  const containerRef = useRef<HTMLDivElement | null>(null)
  const [selectedRun, setSelectedRun] = useState<CompletedRunSummary | null>(null)
  const [runDetail, setRunDetail] = useState<CompletedRunDetail | null>(null)
  const [detailLoading, setDetailLoading] = useState(false)
  const [detailError, setDetailError] = useState<unknown>(null)
  const [reloadKey, setReloadKey] = useState(0)

  useEffect(() => {
    if (open) {
      return
    }
    setSelectedRun(null)
    setRunDetail(null)
    setDetailError(null)
    setDetailLoading(false)
    setReloadKey(0)
  }, [open])

  useEffect(() => {
    if (!open) {
      return
    }
    const originalOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    return () => {
      document.body.style.overflow = originalOverflow
    }
  }, [open])

  useEffect(() => {
    if (!open) {
      return
    }

    const previouslyFocused = document.activeElement as HTMLElement | null
    const container = containerRef.current

    if (!container) {
      return
    }

    const focusFirstElement = () => {
      const focusable = container.querySelectorAll<HTMLElement>(focusableSelectors)
      const target = focusable.length > 0 ? focusable[0] : container
      target.focus()
    }

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault()
        onClose()
        return
      }

      if (event.key !== 'Tab') {
        return
      }

      const focusable = container.querySelectorAll<HTMLElement>(focusableSelectors)
      if (focusable.length === 0) {
        event.preventDefault()
        container.focus()
        return
      }

      const first = focusable[0]
      const last = focusable[focusable.length - 1]

      if (event.shiftKey) {
        if (document.activeElement === first) {
          event.preventDefault()
          last.focus()
        }
      } else if (document.activeElement === last) {
        event.preventDefault()
        first.focus()
      }
    }

    focusFirstElement()
    document.addEventListener('keydown', handleKeyDown)

    return () => {
      document.removeEventListener('keydown', handleKeyDown)
      previouslyFocused?.focus()
    }
  }, [open, onClose])

  useEffect(() => {
    if (!selectedRun) {
      return
    }

    let cancelled = false
    setDetailLoading(true)
    setDetailError(null)

    const load = async () => {
      try {
        const detail = await getCompletedRunDetail(selectedRun.runId)
        if (!cancelled) {
          setRunDetail(detail)
        }
      } catch (error) {
        if (!cancelled) {
          setDetailError(error)
        }
      } finally {
        if (!cancelled) {
          setDetailLoading(false)
        }
      }
    }

    void load()

    return () => {
      cancelled = true
    }
  }, [selectedRun, reloadKey])

  const handleOverlayClick = useCallback(
    (event: MouseEvent<HTMLDivElement>) => {
      if (event.target === event.currentTarget) {
        onClose()
      }
    },
    [onClose],
  )

  const handleSelectRun = useCallback((run: CompletedRunSummary) => {
    setSelectedRun(run)
    setRunDetail(null)
    setDetailError(null)
    setReloadKey((key) => key + 1)
  }, [])

  const handleBackToList = useCallback(() => {
    setSelectedRun(null)
    setRunDetail(null)
    setDetailError(null)
    setDetailLoading(false)
  }, [])

  const handleRetry = useCallback(() => {
    if (selectedRun) {
      setReloadKey((key) => key + 1)
    }
  }, [selectedRun])

  const handleExport = useCallback(() => {
    if (!selectedRun || !runDetail || runDetail.items.length === 0) {
      return
    }

    const title = buildRunTitle(selectedRun, runDetail)
    const csv = `\ufeff${buildCsvContent(title, runDetail)}`
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' })
    const url = URL.createObjectURL(blob)

    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = `${slugify(title)}.csv`
    anchor.setAttribute('data-testid', 'csv-download-link')

    document.body.appendChild(anchor)
    try {
      anchor.click()
    } finally {
      document.body.removeChild(anchor)
      URL.revokeObjectURL(url)
    }
  }, [runDetail, selectedRun])

  const hasCompletedRuns = completedRuns.length > 0
  const orderedCompletedRuns = useMemo(
    () => [...completedRuns].sort((a, b) => (a.completedAtUtc > b.completedAtUtc ? -1 : 1)),
    [completedRuns],
  )

  const detailErrorDescription = useMemo(() => describeDetailError(detailError), [detailError])
  const hasDetailView = Boolean(selectedRun)
  const detailTitle = selectedRun ? buildRunTitle(selectedRun, runDetail) : ''
  const itemsCount = runDetail ? runDetail.items.length : null
  const totalQuantity = useMemo(() => {
    if (!runDetail) {
      return null
    }
    return runDetail.items.reduce((sum, item) => sum + item.quantity, 0)
  }, [runDetail])
  const canExport = Boolean(runDetail && runDetail.items.length > 0)

  if (!open) {
    return null
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center bg-slate-900/60 px-4 py-6 sm:items-center"
      role="presentation"
      onClick={handleOverlayClick}
    >
      <div
        ref={containerRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="completed-runs-modal-title"
        className="relative flex w-full max-w-3xl flex-col overflow-hidden rounded-3xl bg-white shadow-2xl outline-none focus-visible:ring-2 focus-visible:ring-emerald-500 dark:bg-slate-900"
        tabIndex={-1}
      >
        <header className="flex items-start justify-between gap-4 border-b border-emerald-200 bg-emerald-50 px-5 py-4 dark:border-emerald-800/60 dark:bg-emerald-900/20">
          <div>
            <p className="text-xs font-medium uppercase tracking-wide text-emerald-700 dark:text-emerald-200">Progrès</p>
            <h2 id="completed-runs-modal-title" className="mt-1 text-xl font-semibold text-slate-900 dark:text-white">
              Comptages terminés
            </h2>
            <p className="mt-1 text-sm text-slate-600 dark:text-slate-300">
              Consultez les comptages finalisés.
            </p>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="shrink-0 rounded-full border border-emerald-200 p-2 text-emerald-700 transition hover:bg-emerald-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald-500 focus-visible:ring-offset-2 dark:border-emerald-700 dark:text-emerald-200 dark:hover:bg-emerald-800/40"
            aria-label="Fermer"
          >
            <span aria-hidden="true">✕</span>
          </button>
        </header>
        <div className="flex-1 overflow-y-auto px-5 py-6">
          {hasDetailView && selectedRun ? (
            <section className="flex flex-col gap-4">
              <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                <div>
                  <p className="text-xs font-semibold uppercase tracking-wide text-emerald-700 dark:text-emerald-200">
                    Détail du comptage
                  </p>
                  <h3 className="mt-1 text-lg font-semibold text-slate-900 dark:text-white">{detailTitle}</h3>
                  <p className="mt-1 text-sm text-slate-600 dark:text-slate-300">
                    {selectedRun.locationCode} · {selectedRun.locationLabel}
                  </p>
                </div>
                <div className="flex flex-wrap justify-end gap-2">
                  <Button variant="secondary" onClick={handleBackToList}>
                    Retour à la liste
                  </Button>
                  <Button onClick={handleExport} disabled={!canExport}>
                    Exporter en CSV
                  </Button>
                </div>
              </div>

              <div className="rounded-2xl border border-emerald-200 bg-white/70 px-4 py-4 text-sm text-slate-700 shadow-sm dark:border-emerald-800/60 dark:bg-emerald-900/20 dark:text-slate-200">
                <p>
                  Opérateur :{' '}
                  <span className="font-semibold text-slate-900 dark:text-white">
                  {formatOwnerName(runDetail?.ownerDisplayName ?? selectedRun.ownerDisplayName)}
                  </span>
                </p>
                <p className="mt-1">
                  Terminé le{' '}
                  <span className="font-semibold text-slate-900 dark:text-white">
                    {formatDateTimeLong(runDetail?.completedAtUtc ?? selectedRun.completedAtUtc)}
                  </span>
                </p>
                <p className="mt-1">
                  Références :{' '}
                  <span className="font-semibold text-slate-900 dark:text-white">{itemsCount ?? '—'}</span>
                </p>
                <p className="mt-1">
                  Quantité totale :{' '}
                  <span className="font-semibold text-slate-900 dark:text-white">
                    {totalQuantity != null ? formatQuantity(totalQuantity) : '—'}
                  </span>
                </p>
              </div>

              {detailLoading ? (
                <LoadingIndicator label="Chargement du comptage" />
              ) : detailErrorDescription ? (
                <ErrorPanel
                  title={detailErrorDescription.title}
                  details={detailErrorDescription.details}
                  actionLabel="Réessayer"
                  onAction={handleRetry}
                />
              ) : runDetail ? (
                runDetail.items.length > 0 ? (
                  <div className="overflow-hidden rounded-2xl border border-emerald-200 bg-white/80 shadow-sm dark:border-emerald-800/60 dark:bg-emerald-900/20">
                    <table className="min-w-full divide-y divide-emerald-100 dark:divide-emerald-800/40">
                      <thead className="bg-emerald-50/80 text-left text-xs font-semibold uppercase tracking-wide text-emerald-700 dark:bg-emerald-900/20 dark:text-emerald-200">
                        <tr>
                          <th scope="col" className="px-4 py-3">EAN</th>
                          <th scope="col" className="px-4 py-3">Libellé</th>
                          <th scope="col" className="px-4 py-3 text-right">Quantité</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-emerald-100 text-sm text-slate-700 dark:divide-emerald-800/40 dark:text-slate-200">
                        {runDetail.items.map((item) => (
                          <tr key={item.productId}>
                            <td className="whitespace-nowrap px-4 py-3 font-mono text-xs text-slate-600 dark:text-slate-300">
                              {item.ean ?? '—'}
                            </td>
                            <td className="px-4 py-3 text-sm text-slate-800 dark:text-slate-100">{item.name}</td>
                            <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-semibold text-emerald-700 dark:text-emerald-200">
                              {formatQuantity(item.quantity)}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                ) : (
                  <p className="rounded-2xl border border-emerald-200 bg-white/80 px-4 py-4 text-sm text-emerald-700 dark:border-emerald-800/60 dark:bg-emerald-900/20 dark:text-emerald-200">
                    Aucune référence enregistrée pour ce comptage.
                  </p>
                )
              ) : null}
            </section>
          ) : (
            <section className="flex flex-col gap-3">
              <h3 className="text-sm font-semibold uppercase tracking-wide text-emerald-700 dark:text-emerald-200">
                Comptages terminés
              </h3>
              {hasCompletedRuns ? (
                <ul className="space-y-2">
                  {orderedCompletedRuns.map((run) => {
                    const isSelected = selectedRun?.runId === run.runId
                    return (
                      <li key={run.runId}>
                        <button
                          type="button"
                          onClick={() => handleSelectRun(run)}
                          className={clsx(
                            'w-full rounded-2xl border px-4 py-3 text-left transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald-500 focus-visible:ring-offset-2 dark:focus-visible:ring-offset-slate-900',
                            isSelected
                              ? 'border-emerald-300 bg-emerald-100/70 dark:border-emerald-500/40 dark:bg-emerald-500/20'
                              : 'border-transparent bg-white/80 hover:bg-emerald-100/70 dark:border-emerald-800/40 dark:bg-emerald-900/20 dark:hover:bg-emerald-500/20',
                          )}
                        >
                          <p className="text-sm font-semibold text-slate-900 dark:text-white">
                            {run.locationCode} · {run.locationLabel}
                          </p>
                          <p className="mt-1 text-sm text-slate-600 dark:text-slate-300">
                            {describeCountType(run.countType)} • Opérateur : {formatOwnerName(run.ownerDisplayName)} • Terminé le {formatDateTime(run.completedAtUtc)}
                          </p>
                        </button>
                      </li>
                    )
                  })}
                </ul>
              ) : (
                <p className="rounded-2xl border border-emerald-200 bg-white/70 px-4 py-3 text-sm text-emerald-700 dark:border-emerald-800/60 dark:bg-emerald-900/20 dark:text-emerald-200">
                  Aucun comptage terminé récemment.
                </p>
              )}
            </section>
          )}
        </div>
      </div>
    </div>
  )
}
