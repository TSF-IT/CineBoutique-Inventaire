import { clsx } from 'clsx'

import type { ConflictRunHeader, ConflictZoneItem } from '../../types/inventory'

const getProductName = (item: ConflictZoneItem) => {
  const trimmed = item.name?.trim()
  if (trimmed && trimmed.length > 0) {
    return trimmed
  }

  const ean = item.ean?.trim()
  if (ean && ean.length > 0) {
    return `EAN ${ean}`
  }

  const sku = item.sku?.trim()
  if (sku && sku.length > 0) {
    return `SKU ${sku}`
  }

  return 'Référence en conflit'
}

const computeAmplitude = (item: ConflictZoneItem) => {
  const counts = Array.isArray(item.allCounts) ? item.allCounts : []
  if (counts.length === 0) {
    return Math.abs(item.delta)
  }
  const quantities = counts.map((count) => count.quantity)
  if (quantities.length === 0) {
    return Math.abs(item.delta)
  }
  const min = Math.min(...quantities)
  const max = Math.max(...quantities)
  return Math.max(0, max - min)
}

const getDeltaClassName = (delta: number, isAmplitude: boolean) => {
  if (delta === 0) {
    return 'text-slate-600 dark:text-slate-300'
  }

  if (isAmplitude) {
    return 'text-amber-600 dark:text-amber-300'
  }

  return delta > 0 ? 'text-emerald-600 dark:text-emerald-300' : 'text-rose-600 dark:text-rose-300'
}

const buildRunsContainerClass = (stackRuns: boolean) =>
  clsx('conflict-runs', 'conflict-table', stackRuns && 'conflict-runs--stacked')

interface ConflictItemsListProps {
  items: ConflictZoneItem[]
  runs?: ConflictRunHeader[] | null
  stackRuns?: boolean
}

export const ConflictItemsList = ({ items, runs, stackRuns = false }: ConflictItemsListProps) => {
  if (!Array.isArray(items) || items.length === 0) {
    return null
  }

  const normalizedRuns = Array.isArray(runs) ? runs : []
  const hasDynamicColumns = normalizedRuns.length > 0
  const runsContainerClass = buildRunsContainerClass(stackRuns)

  return (
    <div className="conflict-card-stack" data-testid="conflict-items-list">
      {items.map((item, index) => {
        const key =
          item.productId?.trim() ||
          item.ean?.trim() ||
          (item.sku?.trim() ? `sku-${item.sku.trim()}` : `conflict-item-${index}`)

        if (hasDynamicColumns) {
          const countsByRun = new Map<string, number>()
          for (const count of item.allCounts ?? []) {
            if (count?.runId) {
              countsByRun.set(count.runId, count.quantity)
            }
          }
          const amplitude = computeAmplitude(item)

          return (
            <article key={key} className="conflict-card">
              <header className="conflict-card__header">
                <p className="conflict-card__name">{getProductName(item)}</p>
              </header>
              <div className={runsContainerClass}>
                {normalizedRuns.map((run) => {
                  const quantity = countsByRun.get(run.runId) ?? 0
                  return (
                    <section key={run.runId} className="conflict-run">
                      <p className="conflict-run__title">Comptage {run.countType}</p>
                      <p className="conflict-run__quantity">{quantity}</p>
                    </section>
                  )
                })}
                <section className="conflict-run conflict-run--delta">
                  <p className="conflict-run__title">Amplitude</p>
                  <p className={clsx('conflict-run__quantity', getDeltaClassName(amplitude, true))}>
                    {amplitude > 0 ? `±${amplitude}` : amplitude}
                  </p>
                </section>
              </div>
            </article>
          )
        }

        return (
          <article key={key} className="conflict-card">
            <header className="conflict-card__header">
              <p className="conflict-card__name">{getProductName(item)}</p>
            </header>
            <div className={runsContainerClass}>
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
                <p className={clsx('conflict-run__quantity', getDeltaClassName(item.delta, false))}>
                  {item.delta > 0 ? `+${item.delta}` : item.delta}
                </p>
              </section>
            </div>
          </article>
        )
      })}
    </div>
  )
}

