import type { MouseEvent } from 'react'
import { useCallback, useEffect, useMemo, useRef } from 'react'
import type { CompletedRunSummary } from '../../types/inventory'

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

const formatOperator = (name: string | null | undefined) => {
  const trimmed = name?.trim()
  return trimmed && trimmed.length > 0 ? trimmed : '—'
}

export const CompletedRunsModal = ({ open, completedRuns, onClose }: CompletedRunsModalProps) => {
  const containerRef = useRef<HTMLDivElement | null>(null)

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

  const hasCompletedRuns = completedRuns.length > 0
  const orderedCompletedRuns = useMemo(
    () => [...completedRuns].sort((a, b) => (a.completedAtUtc > b.completedAtUtc ? -1 : 1)),
    [completedRuns],
  )

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
              Consultez les 20 derniers comptages finalisés.
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
          <section className="flex flex-col gap-3">
            <h3 className="text-sm font-semibold uppercase tracking-wide text-emerald-700 dark:text-emerald-200">
              Comptages terminés (20 plus récents)
            </h3>
            {hasCompletedRuns ? (
              <ul className="divide-y divide-emerald-100 rounded-2xl border border-emerald-200 bg-white/80 dark:divide-emerald-800/40 dark:border-emerald-800/60 dark:bg-emerald-900/20">
                {orderedCompletedRuns.map((run) => (
                  <li key={run.runId} className="px-4 py-3">
                    <p className="text-sm font-semibold text-slate-900 dark:text-white">
                      {run.locationCode} · {run.locationLabel}
                    </p>
                    <p className="mt-1 text-sm text-slate-600 dark:text-slate-300">
                      {describeCountType(run.countType)} • Opérateur : {formatOperator(run.operatorDisplayName)} • Terminé le {formatDateTime(run.completedAtUtc)}
                    </p>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="rounded-2xl border border-emerald-200 bg-white/70 px-4 py-3 text-sm text-emerald-700 dark:border-emerald-800/60 dark:bg-emerald-900/20 dark:text-emerald-200">
                Aucun comptage terminé récemment.
              </p>
            )}
          </section>
        </div>
      </div>
    </div>
  )
}
