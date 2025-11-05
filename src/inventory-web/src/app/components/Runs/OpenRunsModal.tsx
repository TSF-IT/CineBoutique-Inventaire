import type { MouseEvent } from 'react'
import { useCallback, useEffect, useMemo, useRef } from 'react'

import type { OpenRunSummary } from '../../types/inventory'
import { modalOverlayClassName } from '../Modal/modalOverlayClassName'
import { modalContainerStyle } from '../Modal/modalContainerStyle'
import { ModalPortal } from '../Modal/ModalPortal'

import { formatZoneTitle, resolveZoneLabel, toValidLocationCode } from './runLocation'

interface OpenRunsModalProps {
  open: boolean
  openRuns: OpenRunSummary[]
  onClose: () => void
  ownedRunIds?: string[]
  onResumeRun?: (run: OpenRunSummary) => void
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

const formatOwnerName = (name: string | null | undefined) => {
  const trimmed = name?.trim()
  return trimmed && trimmed.length > 0 ? trimmed : '—'
}

const compareOpenRunsByZone = (a: OpenRunSummary, b: OpenRunSummary) => {
  const zoneA = resolveZoneLabel(a)
  const zoneB = resolveZoneLabel(b)
  const zoneComparison = zoneA.localeCompare(zoneB, 'fr', {
    sensitivity: 'base',
    ignorePunctuation: true,
  })
  if (zoneComparison !== 0) {
    return zoneComparison
  }

  const countTypeA = a.countType ?? Number.MAX_SAFE_INTEGER
  const countTypeB = b.countType ?? Number.MAX_SAFE_INTEGER
  const countTypeComparison = countTypeA - countTypeB
  if (countTypeComparison !== 0) {
    return countTypeComparison
  }

  const codeA = toValidLocationCode(a.locationCode) ?? ''
  const codeB = toValidLocationCode(b.locationCode) ?? ''
  const codeComparison = codeA.localeCompare(codeB, 'fr', {
    sensitivity: 'base',
    ignorePunctuation: true,
  })
  if (codeComparison !== 0) {
    return codeComparison
  }

  const startedA = a.startedAtUtc ?? ''
  const startedB = b.startedAtUtc ?? ''
  const startedComparison = startedA.localeCompare(startedB)
  if (startedComparison !== 0) {
    return startedComparison
  }

  return a.runId.localeCompare(b.runId)
}

export const OpenRunsModal = ({ open, openRuns, onClose, ownedRunIds, onResumeRun }: OpenRunsModalProps) => {
  const containerRef = useRef<HTMLDivElement | null>(null)
  const ownedRunsSet = useMemo(() => new Set(ownedRunIds ?? []), [ownedRunIds])
  const orderedOpenRuns = useMemo(() => [...openRuns].sort(compareOpenRunsByZone), [openRuns])

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

  const hasOpenRuns = openRuns.length > 0
  if (!open) {
    return null
  }

  return (
    <ModalPortal>
      <div className={modalOverlayClassName} role="presentation" onClick={handleOverlayClick}>
        <div
          ref={containerRef}
          role="dialog"
          aria-modal="true"
          aria-labelledby="runs-overview-modal-title"
          className="relative flex w-full max-w-3xl flex-col overflow-hidden rounded-3xl bg-white shadow-2xl outline-none focus-visible:ring-2 focus-visible:ring-brand-500 dark:bg-slate-900"
          tabIndex={-1}
          data-modal-container=""
          style={modalContainerStyle}
        >
        <header className="flex items-start justify-between gap-4 border-b border-slate-200 bg-slate-50 px-5 py-4 dark:border-slate-700 dark:bg-slate-900/70">
          <div>
            <p className="text-xs font-medium uppercase tracking-wide text-brand-600 dark:text-brand-200">
              Suivi des comptages
            </p>
            <h2 id="runs-overview-modal-title" className="mt-1 text-xl font-semibold text-slate-900 dark:text-white">
              Comptages en cours
            </h2>
            <p className="mt-1 text-sm text-slate-600 dark:text-slate-300">
              Visualisez les zones d’inventaire actuellement actives.
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
        <div className="flex-1 min-h-0 overflow-y-auto px-5 py-6">
          <section className="flex flex-col gap-3">
            <h3 className="text-sm font-semibold uppercase tracking-wide text-brand-600 dark:text-brand-200">
              Comptages en cours
            </h3>
            {hasOpenRuns ? (
              <ul className="divide-y divide-slate-200 rounded-2xl border border-slate-200 bg-white/80 dark:divide-slate-800 dark:border-slate-700 dark:bg-slate-900/40">
                {orderedOpenRuns.map((run) => (
                  <li key={run.runId} className="px-4 py-3">
                    <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
                      <div>
                        <p className="text-sm font-semibold text-slate-900 dark:text-white">
                          {formatZoneTitle(run)}
                        </p>
                        <p className="mt-1 text-sm text-slate-600 dark:text-slate-300">
                          {describeCountType(run.countType)} • Opérateur : {formatOwnerName(run.ownerDisplayName)} • Démarré le {formatDateTime(run.startedAtUtc)}
                        </p>
                      </div>
                      {ownedRunsSet.has(run.runId) && typeof onResumeRun === 'function' && (
                        <button
                          type="button"
                          onClick={() => onResumeRun(run)}
                          className="inline-flex shrink-0 items-center gap-2 rounded-full border border-brand-300 px-4 py-1.5 text-xs font-semibold uppercase tracking-wide text-brand-700 transition hover:bg-brand-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500 focus-visible:ring-offset-2 dark:border-brand-400/60 dark:text-brand-100 dark:hover:bg-brand-500/20"
                        >
                          <span aria-hidden="true">⏯</span>
                          <span>Reprendre</span>
                        </button>
                      )}
                    </div>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="rounded-2xl border border-slate-200 bg-white/70 px-4 py-3 text-sm text-slate-600 dark:border-slate-700 dark:bg-slate-900/40 dark:text-slate-300">
                Aucun comptage en cours.
              </p>
            )}
          </section>
        </div>
      </div>
      </div>
    </ModalPortal>
  )
}
