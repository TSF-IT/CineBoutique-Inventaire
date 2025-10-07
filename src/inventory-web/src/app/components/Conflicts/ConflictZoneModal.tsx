import type { MouseEvent } from 'react'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { getConflictZoneDetail } from '../../api/inventoryApi'
import type { ConflictZoneDetail, ConflictZoneSummary } from '../../types/inventory'
import { LoadingIndicator } from '../LoadingIndicator'

interface ConflictZoneModalProps {
  open: boolean
  zone: ConflictZoneSummary | null
  onClose: () => void
}

interface DetailState {
  status: 'idle' | 'loading' | 'loaded' | 'error'
  detail: ConflictZoneDetail | null
  error: Error | null
}

const focusableSelectors = [
  'a[href]',
  'button:not([disabled])',
  'textarea:not([disabled])',
  'input:not([disabled])',
  'select:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
].join(', ')

export const ConflictZoneModal = ({ open, zone, onClose }: ConflictZoneModalProps) => {
  const [state, setState] = useState<DetailState>({ status: 'idle', detail: null, error: null })
  const containerRef = useRef<HTMLDivElement | null>(null)

  useEffect(() => {
    if (!open || !zone) {
      setState((current) =>
        current.status === 'idle' ? current : { status: 'idle', detail: null, error: null },
      )
      return
    }

    const abortController = new AbortController()
    let isMounted = true

    setState({ status: 'loading', detail: null, error: null })

    getConflictZoneDetail(zone.locationId, abortController.signal)
      .then((detail) => {
        if (!isMounted) {
          return
        }
        setState({ status: 'loaded', detail, error: null })
      })
      .catch((error: unknown) => {
        if (!isMounted || abortController.signal.aborted) {
          return
        }
        const err = error instanceof Error ? error : new Error('Erreur inconnue')
        setState({ status: 'error', detail: null, error: err })
      })

    return () => {
      isMounted = false
      abortController.abort()
    }
  }, [open, zone])

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

  const handleOverlayClick = useCallback(
    (event: MouseEvent<HTMLDivElement>) => {
      if (event.target === event.currentTarget) {
        onClose()
      }
    },
    [onClose],
  )

  const { status, detail, error } = state
  const hasItems = detail?.items && detail.items.length > 0

  const headerTitle = useMemo(() => {
    if (!zone) {
      return 'Conflits'
    }
    return `${zone.locationCode} · ${zone.locationLabel}`
  }, [zone])

  if (!open || !zone) {
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
        aria-labelledby="conflict-zone-modal-title"
        className="relative flex w-full max-w-3xl flex-col overflow-hidden rounded-3xl bg-white shadow-2xl outline-none focus-visible:ring-2 focus-visible:ring-brand-500 dark:bg-slate-900"
        tabIndex={-1}
      >
        <header className="flex items-start justify-between gap-4 border-b border-slate-200 bg-slate-50 px-5 py-4 dark:border-slate-700 dark:bg-slate-900/70">
          <div>
            <p className="text-xs font-medium uppercase tracking-wide text-rose-600 dark:text-rose-300">Zone en conflit</p>
            <h2 id="conflict-zone-modal-title" className="mt-1 text-xl font-semibold text-slate-900 dark:text-white">
              {headerTitle}
            </h2>
            <p className="mt-1 text-sm text-slate-600 dark:text-slate-300">
              Comparatif des quantités par comptage.
            </p>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="shrink-0 rounded-full border border-slate-300 p-2 text-slate-600 transition hover:bg-slate-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500 focus-visible:ring-offset-2 dark:border-slate-600 dark:text-slate-200 dark:hover:bg-slate-700"
            aria-label="Fermer"
          >
            <span aria-hidden="true">✕</span>
          </button>
        </header>
        <div className="flex-1 overflow-y-auto px-5 py-6">
          {status === 'loading' && (
            <div className="flex justify-center py-10">
              <LoadingIndicator label="Chargement des divergences" />
            </div>
          )}
          {status === 'error' && (
            <div className="rounded-2xl border border-rose-200 bg-rose-50 p-4 text-sm text-rose-700 dark:border-rose-500/50 dark:bg-rose-500/10 dark:text-rose-100">
              Impossible de charger le détail du conflit. Réessaie.
              {import.meta.env.DEV && error && (
                <pre className="mt-3 overflow-x-auto whitespace-pre-wrap text-xs text-rose-500 dark:text-rose-200">
                  {error.message}
                </pre>
              )}
            </div>
          )}
          {status === 'loaded' && detail && hasItems && (
            <div className="flex flex-col gap-4">
              {detail.items.map((item) => (
                <div
                  key={item.productId}
                  className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm dark:border-slate-700 dark:bg-slate-800"
                >
                  <p className="text-sm font-semibold text-slate-900 dark:text-white">EAN {item.ean}</p>
                  <div className="mt-3 flex flex-wrap gap-3 text-sm">
                    {item.runs.map((run) => (
                      <div key={run.runId} className="min-w-[120px]">
                        <p className="text-xs uppercase text-slate-500 dark:text-slate-400">
                          Comptage {run.countType}
                        </p>
                        <p className="mt-1 font-semibold text-slate-900 dark:text-white">{run.quantity}</p>
                      </div>
                    ))}
                    <div className="min-w-[120px]">
                      <p className="text-xs uppercase text-slate-500 dark:text-slate-400">Écart</p>
                      <p
                        className={`mt-1 font-semibold ${
                          item.delta === 0
                            ? 'text-slate-600 dark:text-slate-300'
                            : item.delta > 0
                              ? 'text-emerald-600 dark:text-emerald-300'
                              : 'text-rose-600 dark:text-rose-300'
                        }`}
                      >
                        {item.delta > 0 ? `+${item.delta}` : item.delta}
                      </p>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}
          {status === 'loaded' && detail && !hasItems && (
            <p className="rounded-2xl border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-700 dark:border-emerald-500/50 dark:bg-emerald-500/10 dark:text-emerald-200">
              Aucun écart détecté entre les deux derniers comptages pour cette zone.
            </p>
          )}
        </div>
        <footer className="flex flex-wrap items-center justify-end gap-3 border-t border-slate-200 bg-slate-50 px-5 py-4 dark:border-slate-700 dark:bg-slate-900/70">
          <button
            type="button"
            onClick={onClose}
            className="rounded-full border border-slate-300 px-4 py-2 text-sm font-medium text-slate-700 transition hover:bg-slate-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500 focus-visible:ring-offset-2 dark:border-slate-600 dark:text-slate-200 dark:hover:bg-slate-700"
          >
            Fermer
          </button>
          <button
            type="button"
            disabled
            className="rounded-full bg-slate-900 px-4 py-2 text-sm font-semibold text-white opacity-60 dark:bg-slate-100 dark:text-slate-900"
          >
            Marquer comme résolu (bientôt)
          </button>
        </footer>
      </div>
    </div>
  )
}
