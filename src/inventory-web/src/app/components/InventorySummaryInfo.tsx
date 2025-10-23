import type { ReactNode } from 'react'
import clsx from 'clsx'

export type InventorySummaryInfoProps = {
  className?: string
  userName?: string | null
  locationLabel?: string | null
  countLabel?: ReactNode
  testId?: string
}

const resolveDisplayValue = (value: ReactNode | null | undefined): ReactNode => {
  if (value === null || value === undefined) {
    return '–'
  }

  if (typeof value === 'string') {
    const trimmed = value.trim()
    return trimmed.length > 0 ? trimmed : '–'
  }

  return value
}

export const InventorySummaryInfo = ({
  className,
  userName,
  locationLabel,
  countLabel,
  testId,
}: InventorySummaryInfoProps) => {
  const items: Array<{ label: string; value: ReactNode }> = [
    { label: 'Utilisateur', value: resolveDisplayValue(userName) },
    { label: 'Zone', value: resolveDisplayValue(locationLabel) },
    { label: 'Comptage', value: resolveDisplayValue(countLabel) },
  ]

  return (
    <dl
      data-testid={testId}
      className={clsx(
        'w-full min-w-[12rem] max-w-xs rounded-xl border border-slate-200/60 bg-white/70 px-4 py-3 text-right text-[0.75rem] text-slate-600 shadow-sm ring-1 ring-black/5 backdrop-blur supports-[backdrop-filter]:bg-white/40 dark:border-slate-700/60 dark:bg-slate-900/40 dark:text-slate-300 sm:w-auto',
        className,
      )}
    >
      {items.map(({ label, value }) => (
        <div key={label} className="flex flex-col gap-1">
          <dt className="text-[0.65rem] font-semibold uppercase tracking-[0.18em] text-slate-500/90 dark:text-slate-400/80">
            {label}
          </dt>
          <dd className="text-sm font-semibold text-slate-900 dark:text-white">{value}</dd>
        </div>
      ))}
    </dl>
  )
}
