import { clsx } from 'clsx'
import {
  useEffect,
  useId,
  useRef,
  useState,
  type ChangeEvent,
  type FocusEvent,
  type KeyboardEvent,
} from 'react'

export interface ScannedRowProps {
  id: string
  label: string
  sku?: string
  ean?: string
  subGroup?: string | null
  qty: number
  hasConflict?: boolean
  highlight?: boolean
  onInc: () => void
  onDec: () => void
  onSetQty: (nextQuantity: number | null) => void
  onOpenConflict?: () => void
}

const sanitizeQuantity = (value: string) => value.replace(/\D+/g, '').slice(0, 4)

export const ScannedRow = ({
  id,
  label,
  sku,
  ean,
  subGroup,
  qty,
  hasConflict,
  highlight,
  onInc,
  onDec,
  onSetQty,
  onOpenConflict,
}: ScannedRowProps) => {
  const inputRef = useRef<HTMLInputElement | null>(null)
  const [draft, setDraft] = useState<string>(String(qty))
  const [isEditing, setIsEditing] = useState(false)
  const quantityInputId = useId()
  const subGroupLabel = subGroup?.trim()

  useEffect(() => {
    if (isEditing) {
      return
    }
    setDraft(String(qty))
  }, [isEditing, qty])

  const handleChange = (event: ChangeEvent<HTMLInputElement>) => {
    setDraft(sanitizeQuantity(event.target.value))
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
    commit(event.currentTarget.value)
  }

  const handleFocus = () => {
    setIsEditing(true)
    requestAnimationFrame(() => {
      inputRef.current?.select()
    })
  }

  const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter') {
      event.preventDefault()
      commit(event.currentTarget.value)
      inputRef.current?.blur()
    }
    if (event.key === 'Escape') {
      event.preventDefault()
      setDraft(String(qty))
      inputRef.current?.blur()
    }
  }

  return (
    <li
      className={clsx(
        'flex flex-col gap-3 rounded-3xl border border-slate-200 bg-white px-4 py-3 shadow-[0_8px_18px_-14px_rgba(15,23,42,0.45)] transition dark:border-slate-700/60 dark:bg-slate-900/70',
        highlight && 'ring-2 ring-brand-300 ring-offset-2 ring-offset-white dark:ring-offset-slate-950',
      )}
      data-testid="scanned-row"
      data-item-id={id}
    >
      <div className="flex flex-col gap-1">
        <label
          htmlFor={quantityInputId}
          className="text-sm font-semibold leading-tight text-slate-900 dark:text-white"
        >
          {label}
        </label>
        <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-slate-500 dark:text-slate-400">
          {subGroupLabel && (
            <span className="truncate text-slate-500 dark:text-slate-400">
              Sous-groupe {subGroupLabel}
            </span>
          )}
          {sku && (
            <span className="font-mono uppercase tracking-wide text-slate-500 dark:text-slate-400">
              SKU {sku}
            </span>
          )}
          {(ean || id) && (
            <span className="font-mono uppercase tracking-wide text-slate-400 dark:text-slate-500">
              {ean ? `EAN ${ean}` : `ID ${id}`}
            </span>
          )}
          {hasConflict &&
            (onOpenConflict ? (
              <button
                type="button"
                className="rounded-full bg-rose-100 px-2 py-1 text-[11px] font-semibold uppercase tracking-wide text-rose-700 hover:bg-rose-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-rose-400"
                onClick={onOpenConflict}
                aria-label={`Voir le conflit pour ${label}`}
              >
                Conflit
              </button>
            ) : (
              <span className="rounded-full bg-rose-100 px-2 py-1 text-[11px] font-semibold uppercase tracking-wide text-rose-700 dark:bg-rose-500/10 dark:text-rose-200">
                Conflit
              </span>
            ))}
        </div>
      </div>
      <div className="flex items-center gap-2">
        <button
          type="button"
          className="flex h-11 w-11 items-center justify-center rounded-full bg-slate-100 text-xl font-semibold text-slate-800 shadow-sm transition active:scale-95 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-300 dark:bg-slate-800 dark:text-white"
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
          className="h-11 w-20 rounded-2xl border border-slate-300 bg-white text-center text-lg font-semibold text-slate-900 shadow-sm focus-visible:border-brand-400 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-200 dark:border-slate-600 dark:bg-slate-800 dark:text-white"
          autoComplete="off"
          maxLength={4}
        />
        <button
          type="button"
          className="flex h-11 w-11 items-center justify-center rounded-full bg-brand-600 text-xl font-semibold text-white shadow-sm transition active:scale-95 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-300"
          onClick={onInc}
          aria-label={`Augmenter la quantité pour ${label}`}
        >
          +
        </button>
      </div>
    </li>
  )
}

ScannedRow.displayName = 'ScannedRow'
