import {
  forwardRef,
  startTransition,
  useEffect,
  useId,
  useImperativeHandle,
  useRef,
  useState,
  type ChangeEvent,
  type FocusEvent,
  type KeyboardEvent,
} from 'react'
import clsx from 'clsx'

export interface ScannedRowHandle {
  focusQuantity: () => void
  scrollIntoView: (options?: ScrollIntoViewOptions) => void
}

type ScannedRowDensity = 'regular' | 'dense'

export interface ScannedRowProps {
  id: string
  label: string
  sku?: string
  ean?: string
  qty: number
  hasConflict?: boolean
  highlight?: boolean
  density?: ScannedRowDensity
  onInc: () => void
  onDec: () => void
  onSetQty: (nextQuantity: number | null) => void
  onOpenConflict?: () => void
  onQuantityFocusChange?: (focused: boolean, rowKey: string) => void
}

const sanitizeQuantity = (value: string) => value.replace(/\D+/g, '').slice(0, 4)

export const ScannedRow = forwardRef<ScannedRowHandle, ScannedRowProps>(
  (
    {
      id,
      label,
      sku,
      ean,
      qty,
      hasConflict,
      highlight,
      density = 'regular',
      onInc,
      onDec,
      onSetQty,
      onOpenConflict,
      onQuantityFocusChange,
    },
    ref,
  ) => {
    const rowRef = useRef<HTMLLIElement | null>(null)
    const inputRef = useRef<HTMLInputElement | null>(null)
    const [draft, setDraft] = useState<string>(String(qty))
    const [isEditing, setIsEditing] = useState(false)
    const quantityInputId = useId()
    const focusKey = ean ?? id
    const isDense = density === 'dense'

    useImperativeHandle(
      ref,
      () => ({
        focusQuantity: () => {
          const input = inputRef.current
          if (!input) return
          input.focus({ preventScroll: true })
          input.select()
        },
        scrollIntoView: (options?: ScrollIntoViewOptions) => {
          const row = rowRef.current
          if (!row) return
          row.scrollIntoView(options)
        },
      }),
      [],
    )

    useEffect(() => {
      if (isEditing) {
        return
      }
      startTransition(() => {
        setDraft(String(qty))
      })
    }, [isEditing, qty])

    const handleChange = (event: ChangeEvent<HTMLInputElement>) => {
      const sanitized = sanitizeQuantity(event.target.value)
      setDraft(sanitized)
    }

    const commit = (raw: string) => {
      const sanitized = sanitizeQuantity(raw)
      if (!sanitized) {
        setDraft('')
        onSetQty(null)
        return
      }
      const parsed = Number.parseInt(sanitized, 10)
      if (!Number.isFinite(parsed) || parsed <= 0) {
        onSetQty(0)
        return
      }
      onSetQty(parsed)
    }

    const handleBlur = (event: FocusEvent<HTMLInputElement>) => {
      setIsEditing(false)
      onQuantityFocusChange?.(false, focusKey)
      commit(event.currentTarget.value)
    }

    const handleFocus = () => {
      setIsEditing(true)
      onQuantityFocusChange?.(true, focusKey)
      requestAnimationFrame(() => {
        inputRef.current?.select()
      })
    }

    const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
      if (event.key === 'Enter') {
        event.preventDefault()
        commit(event.currentTarget.value)
      }
      if (event.key === 'Escape') {
        event.preventDefault()
        setDraft(String(qty))
        inputRef.current?.blur()
      }
    }

    const baseRowClass = clsx(
      'flex items-center justify-between rounded-2xl border px-4 transition-shadow duration-150',
      'border-slate-200 bg-white dark:border-slate-600/60 dark:bg-slate-900/70',
      isDense ? 'gap-2.5 py-2' : 'gap-3 py-3',
      highlight && 'ring-2 ring-emerald-400/70 ring-offset-2 ring-offset-transparent',
    )

    return (
      <li ref={rowRef} className={baseRowClass} data-testid="scanned-row" data-item-id={id}>
        <div className="flex min-w-0 flex-1 flex-col">
          <label
            htmlFor={quantityInputId}
            className={clsx(
              'font-semibold text-slate-900 dark:text-white',
              isDense ? 'text-[13px] leading-5' : 'text-sm',
            )}
          >
            {label}
          </label>
          <div
            className={clsx(
              'flex flex-wrap items-center gap-2 text-slate-500 dark:text-slate-400',
              isDense ? 'text-[11px]' : 'text-xs',
            )}
          >
            {sku && (
              <span className="font-mono uppercase tracking-wide text-slate-500 dark:text-slate-400">SKU {sku}</span>
            )}
            {(ean || id) && (
              <span className="truncate font-mono text-[11px] uppercase tracking-wide text-slate-400">
                {ean ? `EAN ${ean}` : `ID ${id}`}
              </span>
            )}
            {hasConflict && (
              <button
                type="button"
                className="ml-auto inline-flex items-center rounded-full bg-rose-100 px-2 py-1 text-[11px] font-semibold uppercase tracking-wide text-rose-700 hover:bg-rose-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-rose-400"
                onClick={onOpenConflict}
                aria-label={`Voir le conflit pour ${label}`}
              >
                Conflit
              </button>
            )}
          </div>
        </div>
        <div className="flex items-center gap-2">
          <button
            type="button"
            className="flex h-11 min-h-[44px] min-w-[44px] w-11 items-center justify-center rounded-full bg-slate-100 text-xl font-semibold text-slate-800 shadow-sm transition active:scale-95 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-400 dark:bg-slate-800 dark:text-white"
            onClick={onDec}
            aria-label={`Diminuer la quantité pour ${label}`}
          >
            −
          </button>
          <input
            ref={inputRef}
            id={quantityInputId}
            type="text"
            inputMode="numeric"
            pattern="[0-9]*"
            value={draft}
            onChange={handleChange}
            onBlur={handleBlur}
            onFocus={handleFocus}
            onKeyDown={handleKeyDown}
            aria-label={`Quantité pour ${label}`}
            className="h-11 min-h-[44px] w-16 min-w-[56px] max-w-[64px] rounded-xl border border-slate-300 bg-white text-center text-lg font-semibold text-slate-900 shadow-sm focus-visible:border-brand-400 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-200 dark:border-slate-600 dark:bg-slate-800 dark:text-white"
            autoComplete="off"
            maxLength={4}
          />
          <button
            type="button"
            className="flex h-11 min-h-[44px] min-w-[44px] w-11 items-center justify-center rounded-full bg-brand-500 text-xl font-semibold text-white shadow-sm transition active:scale-95 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-300"
            onClick={onInc}
            aria-label={`Augmenter la quantité pour ${label}`}
          >
            +
          </button>
        </div>
      </li>
    )
  },
)

ScannedRow.displayName = 'ScannedRow'

