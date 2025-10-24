import {
  useCallback,
  useEffect,
  useId,
  useMemo,
  useRef,
  useState,
  type KeyboardEvent as ReactKeyboardEvent,
} from 'react'
import { useProductsSearch, type ProductRow } from '@/hooks/useProductsSearch'

export type CatalogueSearchDialogProps = {
  open: boolean
  onClose: () => void
  onPick: (product: ProductRow) => Promise<boolean> | boolean
}

const escapeRegExp = (value: string) => value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')

const computeHighlightedParts = (text: string, query: string) => {
  const normalizedQuery = query.trim()
  if (!normalizedQuery) {
    return [{ value: text, highlighted: false }]
  }

  const regex = new RegExp(escapeRegExp(normalizedQuery), 'gi')
  const segments: Array<{ value: string; highlighted: boolean }> = []
  let lastIndex = 0
  let match: RegExpExecArray | null

  while ((match = regex.exec(text)) !== null) {
    const matchIndex = match.index
    if (matchIndex > lastIndex) {
      segments.push({ value: text.slice(lastIndex, matchIndex), highlighted: false })
    }
    segments.push({ value: match[0], highlighted: true })
    lastIndex = matchIndex + match[0].length
  }

  if (lastIndex < text.length) {
    segments.push({ value: text.slice(lastIndex), highlighted: false })
  }

  if (segments.length === 0) {
    return [{ value: text, highlighted: false }]
  }

  return segments
}

export const CatalogueSearchDialog = ({ open, onClose, onPick }: CatalogueSearchDialogProps) => {
  const inputRef = useRef<HTMLInputElement | null>(null)
  const listRef = useRef<HTMLUListElement | null>(null)
  const [query, setQuery] = useState('')
  const [highlightedIndex, setHighlightedIndex] = useState(0)
  const [pendingSku, setPendingSku] = useState<string | null>(null)
  const listboxId = useId()
  const labelId = useId()

  const activeQuery = open ? query : ''
  const { rows, loading } = useProductsSearch(activeQuery, 200, {
    pageSize: 20,
    sortKey: 'name',
    sortDir: 'asc',
  })

  useEffect(() => {
    if (!open) {
      setQuery('')
      setHighlightedIndex(0)
      setPendingSku(null)
      return
    }
    const focusTimeout = window.setTimeout(() => {
      inputRef.current?.focus()
      inputRef.current?.select()
    }, 0)
    return () => window.clearTimeout(focusTimeout)
  }, [open])

  useEffect(() => {
    if (!open) {
      return
    }
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault()
        onClose()
      }
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => {
      window.removeEventListener('keydown', handleKeyDown)
    }
  }, [open, onClose])

  useEffect(() => {
    setHighlightedIndex(0)
  }, [rows])

  const handleHighlight = useCallback((index: number) => {
    setHighlightedIndex((prev) => {
      if (index === prev) {
        return prev
      }
      const bounded = Math.max(0, Math.min(rows.length - 1, index))
      if (bounded >= 0) {
        const list = listRef.current
        const element = list?.children[bounded] as HTMLElement | undefined
        element?.scrollIntoView({ block: 'nearest' })
      }
      return bounded
    })
  }, [rows.length])

  const handleSelect = useCallback(
    async (result: ProductRow) => {
      if (pendingSku) {
        return
      }
      setPendingSku(result.sku)
      const success = await onPick(result)
      if (success) {
        setPendingSku(null)
        onClose()
        setQuery('')
      } else {
        setPendingSku(null)
      }
    },
    [onPick, onClose, pendingSku],
  )

  const handleInputKeyDown = useCallback(
    (event: ReactKeyboardEvent<HTMLInputElement>) => {
      if (event.key === 'ArrowDown') {
        event.preventDefault()
        handleHighlight(Math.min(highlightedIndex + 1, rows.length - 1))
        return
      }
      if (event.key === 'ArrowUp') {
        event.preventDefault()
        handleHighlight(Math.max(highlightedIndex - 1, 0))
        return
      }
      if (event.key === 'Enter') {
        event.preventDefault()
        const candidate = rows[highlightedIndex] ?? rows[0]
        if (candidate) {
          void handleSelect(candidate)
        }
      }
    },
    [handleHighlight, handleSelect, highlightedIndex, rows],
  )

  const renderHighlightedText = useCallback(
    (text: string) => {
      return computeHighlightedParts(text, query).map((segment, index) => (
        <span
          key={`${segment.value}-${index}`}
          className={segment.highlighted ? 'text-brand-600 dark:text-brand-300' : undefined}
        >
          {segment.value}
        </span>
      ))
    },
    [query],
  )

  const suggestionMessage = useMemo(() => {
    if (!query.trim()) {
      return 'Saisissez un nom, un EAN ou un SKU pour commencer la recherche.'
    }
    if (loading) {
      return 'Recherche en cours…'
    }
    if (!loading && rows.length === 0) {
      return 'Aucun produit trouvé.'
    }
    return null
  }, [loading, query, rows.length])

  if (!open) {
    return null
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 p-4"
      role="dialog"
      aria-modal="true"
      aria-labelledby={labelId}
    >
      <div className="flex w-full max-w-2xl flex-col gap-4 rounded-3xl bg-white p-6 shadow-2xl dark:bg-slate-900">
        <div className="flex items-start justify-between gap-4">
          <div className="space-y-1">
            <h2 id={labelId} className="text-lg font-semibold text-slate-900 dark:text-white">
              Catalogue produits
            </h2>
            <p className="text-sm text-slate-500 dark:text-slate-400">
              Ajoutez un article sans le scanner. La recherche correspond aux noms, EAN et SKU.
            </p>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="rounded-full bg-slate-100 px-3 py-1 text-sm font-medium text-slate-600 transition hover:bg-slate-200 dark:bg-slate-800 dark:text-slate-200 dark:hover:bg-slate-700"
          >
            Fermer
          </button>
        </div>
        <div className="space-y-3">
          <label className="block text-sm font-medium text-slate-700 dark:text-slate-300">
            Recherche dans le catalogue
            <input
              ref={inputRef}
              type="search"
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              onKeyDown={handleInputKeyDown}
              autoComplete="off"
              autoCapitalize="none"
              spellCheck={false}
              placeholder="Saisir un produit (contains)…"
              className="mt-2 w-full rounded-2xl border border-slate-200 px-4 py-3 text-base shadow-inner transition focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-200 dark:border-slate-700 dark:bg-slate-800 dark:text-white"
              role="combobox"
              aria-expanded={rows.length > 0}
              aria-controls={listboxId}
              aria-activedescendant={rows[highlightedIndex] ? `${listboxId}-${rows[highlightedIndex].sku}` : undefined}
              disabled={Boolean(pendingSku)}
            />
          </label>
          <div className="max-h-72 overflow-y-auto rounded-2xl border border-slate-100 bg-slate-50 shadow-inner dark:border-slate-700 dark:bg-slate-800">
            <ul ref={listRef} id={listboxId} role="listbox" className="divide-y divide-slate-100 dark:divide-slate-700">
              {rows.map((product, index) => {
                const isActive = index === highlightedIndex
                const isPending = pendingSku === product.sku
                return (
                  <li key={product.sku} role="presentation">
                    <button
                      type="button"
                      onMouseEnter={() => handleHighlight(index)}
                      onFocus={() => handleHighlight(index)}
                      onClick={() => void handleSelect(product)}
                      role="option"
                      aria-selected={isActive}
                      id={`${listboxId}-${product.sku}`}
                      disabled={isPending}
                      className={`flex w-full items-start gap-3 px-4 py-3 text-left transition ${
                        isActive
                          ? 'bg-white shadow-sm ring-1 ring-brand-200 dark:bg-slate-900 dark:ring-brand-400'
                          : 'hover:bg-white hover:shadow-sm dark:hover:bg-slate-900'
                      } ${isPending ? 'opacity-60' : ''}`}
                    >
                      <div className="flex-1">
                        <p className="text-sm font-semibold text-slate-900 dark:text-white">
                          {renderHighlightedText(product.name)}
                        </p>
                        <p className="text-xs text-slate-500 dark:text-slate-400">
                          {renderHighlightedText(product.sku)}
                          {product.ean ? <span> • {renderHighlightedText(product.ean)}</span> : null}
                        </p>
                        {(product.group || product.subGroup) && (
                          <p className="mt-1 text-xs text-slate-400 dark:text-slate-500">
                            {[product.group, product.subGroup].filter(Boolean).join(' • ')}
                          </p>
                        )}
                      </div>
                      <span className="rounded-full bg-brand-100 px-2 py-0.5 text-xs font-semibold text-brand-600 dark:bg-brand-500/20 dark:text-brand-200">
                        Ajouter
                      </span>
                    </button>
                  </li>
                )
              })}
            </ul>
            {suggestionMessage && (
              <div className="px-4 py-6 text-center text-sm text-slate-500 dark:text-slate-400">
                {suggestionMessage}
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  )
}
