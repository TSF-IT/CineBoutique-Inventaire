import type { MouseEvent } from 'react'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import clsx from 'clsx'
import { getConflictZoneDetail } from '../../api/inventoryApi'
import type { ConflictZoneDetail, ConflictZoneItem, ConflictZoneSummary } from '../../types/inventory'
import { LoadingIndicator } from '../LoadingIndicator'
import { Button } from '../ui/Button'
import { useOrientation } from '../../hooks/useOrientation'

interface ConflictZoneModalProps {
  open: boolean
  zone: ConflictZoneSummary | null
  onClose: () => void
  onStartExtraCount?: (zone: ConflictZoneSummary, nextCountType: number) => void
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

const formatOrdinalFr = (value: number) => {
  if (value <= 0 || Number.isNaN(value)) {
    return `${value}`
  }
  return value === 1 ? '1er' : `${value}ᵉ`
}

const formatSku = (value: string | null | undefined) => {
  const trimmed = value?.trim()
  return trimmed && trimmed.length > 0 ? trimmed : '—'
}

export const ConflictZoneModal = ({ open, zone, onClose, onStartExtraCount }: ConflictZoneModalProps) => {
  const [state, setState] = useState<DetailState>({ status: 'idle', detail: null, error: null })
  const containerRef = useRef<HTMLDivElement | null>(null)
  const [isCompact, setIsCompact] = useState(() => {
    if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
      return false
    }
    return window.matchMedia('(max-width: 720px)').matches
  })
  const orientation = useOrientation()

  useEffect(() => {
    if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
      return
    }

    const mediaQuery = window.matchMedia('(max-width: 720px)')
    const handleChange = (event: MediaQueryListEvent) => {
      setIsCompact(event.matches)
    }

    setIsCompact(mediaQuery.matches)

    if (typeof mediaQuery.addEventListener === 'function') {
      mediaQuery.addEventListener('change', handleChange)
      return () => mediaQuery.removeEventListener('change', handleChange)
    }

    mediaQuery.addListener(handleChange)
    return () => mediaQuery.removeListener(handleChange)
  }, [])

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
  const items = detail?.items ?? []
  const runs = detail?.runs ?? []
  const hasItems = items.length > 0
  const hasDynamicColumns =
    runs.length > 0 && items.some((item) => Array.isArray(item.allCounts) && item.allCounts.length > 0)
  const nextCountType = useMemo(() => {
    if (!detail) {
      return null
    }
    const maxCount = (detail.runs ?? []).reduce((acc, run) => {
      const numeric = Number(run.countType) || 0
      return numeric > acc ? numeric : acc
    }, 0)
    const computed = maxCount > 0 ? maxCount + 1 : 3
    return computed < 3 ? 3 : computed
  }, [detail])

  const formatOwnerName = (value: string | null | undefined) => {
    const trimmed = typeof value === 'string' ? value.trim() : ''
    return trimmed.length > 0 ? trimmed : '—'
  }

  const formatDateTime = (value: string | null | undefined) => {
    if (!value) {
      return '—'
    }

    const parsed = new Date(value)
    if (Number.isNaN(parsed.getTime())) {
      return '—'
    }

    return new Intl.DateTimeFormat('fr-FR', {
      dateStyle: 'short',
      timeStyle: 'short',
    }).format(parsed)
  }

  const getDeltaClassName = (delta: number) => {
    if (delta === 0) {
      return 'text-slate-600 dark:text-slate-300'
    }

    return delta > 0 ? 'text-emerald-600 dark:text-emerald-300' : 'text-rose-600 dark:text-rose-300'
  }

  const headerTitle = useMemo(() => {
    if (!zone) {
      return 'Conflits'
    }
    return `${zone.locationCode} · ${zone.locationLabel}`
  }, [zone])

  const modalClassName = clsx('modal', 'conflict-modal', isCompact ? 'modal-full' : 'modal-window')
  const shouldStackRuns = isCompact || (orientation === 'portrait' && runs.length > 3)
  const runsContainerClass = clsx('conflict-runs', 'conflict-table', shouldStackRuns && 'conflict-runs--stacked')

  const renderRunSections = (item: ConflictZoneItem) => {
    if (hasDynamicColumns) {
      const countsByRun = new Map<string, number>()
      for (const count of item.allCounts ?? []) {
        countsByRun.set(count.runId, count.quantity)
      }

      return (
        <>
          {runs.map((run) => {
            const quantity = countsByRun.get(run.runId) ?? 0
            return (
              <section key={run.runId} className="conflict-run">
                <p className="conflict-run__title">Comptage {run.countType}</p>
                <p className="conflict-run__quantity">{quantity}</p>
                <p className="conflict-run__meta">{formatOwnerName(run.ownerDisplayName)}</p>
                <p className="conflict-run__meta">{formatDateTime(run.completedAtUtc)}</p>
              </section>
            )
          })}
          <section className="conflict-run conflict-run--delta">
            <p className="conflict-run__title">Écart</p>
            <p className={clsx('conflict-run__quantity', getDeltaClassName(item.delta))}>
              {item.delta > 0 ? `+${item.delta}` : item.delta}
            </p>
          </section>
        </>
      )
    }

    return (
      <>
        <section className="conflict-run">
          <p className="conflict-run__title">Comptage 1</p>
          <p className="conflict-run__quantity">{item.qtyC1}</p>
        </section>
        <section className="conflict-run">
          <p className="conflict-run__title">Comptage 2</p>
          <p className="conflict-run__quantity">{item.qtyC2}</p>
        </section>
        <section className="conflict-run conflict-run--delta">
          <p className="conflict-run__title">Écart</p>
          <p className={clsx('conflict-run__quantity', getDeltaClassName(item.delta))}>
            {item.delta > 0 ? `+${item.delta}` : item.delta}
          </p>
        </section>
      </>
    )
  }

  if (!open || !zone) {
    return null
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-stretch justify-center bg-slate-900/60 px-4 py-6 sm:items-center"
      role="presentation"
      onClick={handleOverlayClick}
    >
      <div
        ref={containerRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="conflict-zone-modal-title"
        className={modalClassName}
        tabIndex={-1}
      >
        <header className="modal-header">
          <div>
            <p className="modal-subtitle">Zone en conflit</p>
            <h2 id="conflict-zone-modal-title" className="modal-title">
              {headerTitle}
            </h2>
            <p className="modal-description">Comparatif des quantités par comptage.</p>
          </div>
          <button type="button" onClick={onClose} className="modal-close" aria-label="Fermer">
            <span aria-hidden="true">✕</span>
          </button>
        </header>
        <div className="modal-body">
          {status === 'loading' && (
            <div className="modal-loading">
              <LoadingIndicator label="Chargement des divergences" />
            </div>
          )}
          {status === 'error' && (
            <div className="modal-error">
              Impossible de charger le détail du conflit. Réessaie.
              {import.meta.env.DEV && error && (
                <pre className="modal-error__details">{error.message}</pre>
              )}
            </div>
          )}
          {status === 'loaded' && detail && hasItems && (
            <div className="conflict-card-stack">
              {items.map((item) => (
                <article key={item.productId} className="conflict-card">
                  <header className="conflict-card__header">
                    <p className="conflict-card__ean">
                      <span className="conflict-card__label">EAN</span> {item.ean}
                    </p>
                    <p className="conflict-card__ean">
                      <span className="conflict-card__label">SKU</span> {formatSku(item.sku)}
                    </p>
                  </header>
                  <div className={runsContainerClass}>{renderRunSections(item)}</div>
                </article>
              ))}
            </div>
          )}
          {status === 'loaded' && detail && !hasItems && (
            <p className="conflict-empty">Aucun écart détecté entre les comptages pour cette zone.</p>
          )}
        </div>
        <footer className="modal-footer">
          <div className="flex flex-col gap-3 sm:flex-row sm:justify-end">
            {onStartExtraCount && zone && status === 'loaded' && nextCountType && (
              <Button
                type="button"
                onClick={() => onStartExtraCount(zone, nextCountType)}
                fullWidth={isCompact}
                className="sm:order-2"
              >
                Lancer le {formatOrdinalFr(nextCountType)} comptage
              </Button>
            )}
            <Button
              type="button"
              variant="secondary"
              fullWidth={isCompact}
              onClick={onClose}
              className="sm:order-1"
            >
              Fermer
            </Button>
          </div>
        </footer>
      </div>
    </div>
  )
}
