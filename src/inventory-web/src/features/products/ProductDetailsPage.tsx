import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { fetchProductByEan } from '@/app/api/inventoryApi'
import type { Product } from '@/app/types/inventory'
import { LoadingIndicator } from '@/app/components/LoadingIndicator'

interface ProductAttribute {
  label: string
  value: string
}

export function ProductDetailsPage() {
  const { ean } = useParams<{ ean: string }>()
  const [data, setData] = useState<Product | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let disposed = false

    const load = async () => {
      const trimmed = (ean ?? '').trim()
      if (trimmed.length === 0) {
        setData(null)
        setError('EAN manquant')
        return
      }

      setLoading(true)
      setError(null)
      try {
        const product = await fetchProductByEan(trimmed)
        if (!disposed) {
          setData(product)
        }
      } catch (err) {
        const message = err instanceof Error ? err.message : 'Erreur inconnue'
        if (!disposed) {
          setError(message)
        }
      } finally {
        if (!disposed) {
          setLoading(false)
        }
      }
    }

    load()

    return () => {
      disposed = true
    }
  }, [ean])

  const attrs = useMemo<ProductAttribute[]>(() => {
    if (!data) {
      return []
    }

    const formattedLastCount = (() => {
      if (!data.lastCountedAt) return 'Jamais'
      try {
        const date = new Date(data.lastCountedAt)
        if (Number.isNaN(date.getTime())) return data.lastCountedAt
        return new Intl.DateTimeFormat('fr-FR', {
          dateStyle: 'medium',
          timeStyle: 'short',
        }).format(date)
      } catch {
        return data.lastCountedAt
      }
    })()

    return [
      { label: 'EAN', value: data.ean ?? '—' },
      { label: 'SKU', value: data.sku ?? '—' },
      { label: 'Nom', value: data.name ?? '—' },
      { label: 'Stock', value: typeof data.stock === 'number' ? `${data.stock}` : '—' },
      { label: 'Dernier comptage', value: formattedLastCount },
    ]
  }, [data])

  const nav = useNavigate()
  function copy(txt?: string | null) {
    if (!txt) return
    navigator.clipboard?.writeText(txt).catch(() => {})
  }

  return (
    <div style={{ display: 'grid', gap: 16 }}>
      <h2>Détails du produit</h2>
      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
        <button type="button" onClick={() => nav(-1)}>
          Retour
        </button>
        <button type="button" onClick={() => copy(data?.sku)}>
          Copier SKU
        </button>
        <button type="button" onClick={() => copy(data?.ean ?? undefined)} disabled={!data?.ean}>
          Copier EAN
        </button>
      </div>
      {loading && (
        <div>
          <LoadingIndicator label="Chargement du produit…" />
        </div>
      )}
      {error && (
        <div role="alert" style={{ color: '#b91c1c' }}>
          {error}
        </div>
      )}
      {!loading && !error && attrs.length === 0 && (
        <div style={{ opacity: 0.7 }}>Aucune information disponible.</div>
      )}
      {attrs.length > 0 && (
        <dl
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))',
            gap: 12,
          }}
        >
          {attrs.map((attr) => (
            <div
              key={attr.label}
              style={{
                border: '1px solid #e2e8f0',
                borderRadius: 8,
                padding: 12,
                background: '#fff',
              }}
            >
              <dt style={{ fontWeight: 600, marginBottom: 4 }}>{attr.label}</dt>
              <dd style={{ margin: 0 }}>{attr.value}</dd>
            </div>
          ))}
        </dl>
      )}
    </div>
  )
}
