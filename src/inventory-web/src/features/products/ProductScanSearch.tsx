import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useProductSuggest } from '../../hooks/useProductSuggest'
import { useProductsSearch } from '../../hooks/useProductsSearch'
import { BarcodeCameraButton } from './BarcodeCameraButton'

type Mode = 'scan' | 'camera' | 'manuel'

export function ProductScanSearch(props: { onPick?: (sku: string) => void }) {
  const [mode, setMode] = useState<Mode>('scan')
  const [q, setQ] = useState('')
  const inputRef = useRef<HTMLInputElement>(null)
  const nav = useNavigate()

  const { data: suggest, loading: loadingSuggest } = useProductSuggest(q, 8, 120)
  const [filter, setFilter] = useState('')
  const { rows, loading: loadingSearch } = useProductsSearch(filter, 200)

  useEffect(() => {
    inputRef.current?.focus()
  }, [mode])

  useEffect(() => {
    if (q.trim().length >= 5 && suggest.length === 1 && props.onPick) {
      props.onPick(suggest[0].sku)
    }
  }, [q, suggest, props])

  useEffect(() => {
    if (q.trim().length >= 5 && suggest.length === 1 && !props.onPick) {
      nav(`/products/${encodeURIComponent(suggest[0].sku)}`)
    }
  }, [q, suggest, props, nav])

  const list = useMemo(() => suggest.slice(0, 8), [suggest])

  return (
    <div style={{ display: 'grid', gap: 12 }}>
      <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
        <strong>Mode :</strong>
        <label>
          <input
            type="radio"
            name="mode"
            checked={mode === 'scan'}
            onChange={() => setMode('scan')}
          />{' '}
          Douchette
        </label>
        <label>
          <input
            type="radio"
            name="mode"
            checked={mode === 'camera'}
            onChange={() => setMode('camera')}
          />{' '}
          Caméra
        </label>
        <label>
          <input
            type="radio"
            name="mode"
            checked={mode === 'manuel'}
            onChange={() => setMode('manuel')}
          />{' '}
          Manuel
        </label>
      </div>

      {(mode === 'scan' || mode === 'camera') && (
        <div style={{ display: 'grid', gap: 8 }}>
          <BarcodeCameraButton onDetected={(val) => setQ(val)} />
          <input
            ref={inputRef}
            value={q}
            onChange={(e) => setQ(e.target.value)}
            placeholder="Scannez ou tapez un produit…"
            aria-label="Recherche produits (scan)"
            autoCapitalize="off"
            autoCorrect="off"
            autoComplete="off"
            inputMode="text"
            onKeyDown={(e) => {
              if (e.key === 'Enter' && list.length >= 1 && props.onPick) {
                props.onPick(list[0].sku)
              }
            }}
          />
          {loadingSuggest && <div>Recherche…</div>}
          {!loadingSuggest && list.length > 1 && (
            <ul>
              {list.map((x) => (
                <li key={x.sku}>
                  <button type="button" onClick={() => props.onPick?.(x.sku)}>
                    {x.ean ? `[${x.ean}] ` : ''}
                    {x.name} — {x.sku}
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      <div style={{ display: 'grid', gap: 6, marginTop: 8 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <strong>Ou choisir dans la liste</strong>
          <span
            style={{
              borderRadius: 12,
              padding: '2px 8px',
              background: '#eee',
              fontSize: 12,
            }}
          >
            {loadingSearch ? '…' : rows.length} items
          </span>
        </div>
        <input
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          placeholder="Filtrer (contains)…"
          aria-label="Filtre produits"
        />
        <div style={{ overflowX: 'auto' }}>
          <table style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead>
              <tr>
                <th style={{ textAlign: 'left' }}>EAN</th>
                <th style={{ textAlign: 'left' }}>SKU</th>
                <th style={{ textAlign: 'left' }}>Nom</th>
                <th style={{ textAlign: 'left' }}>Groupe</th>
                <th style={{ textAlign: 'left' }}>Sous-groupe</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {rows.map((p) => (
                <tr key={p.sku}>
                  <td>{p.ean ?? ''}</td>
                  <td>{p.sku}</td>
                  <td>{p.name}</td>
                  <td>{p.group ?? ''}</td>
                  <td>{p.subGroup ?? ''}</td>
                  <td>
                    <button type="button" onClick={() => props.onPick?.(p.sku)}>
                      Choisir
                    </button>
                  </td>
                </tr>
              ))}
              {!loadingSearch && rows.length === 0 && (
                <tr>
                  <td colSpan={6} style={{ opacity: 0.7 }}>
                    Aucun produit
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  )
}
