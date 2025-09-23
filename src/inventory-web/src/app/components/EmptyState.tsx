import type { ReactNode } from 'react'
import clsx from 'clsx'
import { Button } from './ui/Button'

interface EmptyStateProps {
  icon?: ReactNode
  title: string
  description?: string
  className?: string
  actionLabel?: string
  onAction?: () => void
}

export const EmptyState = ({ icon, title, description, className, actionLabel, onAction }: EmptyStateProps) => (
  <div
    className={clsx(
      'flex flex-col items-center justify-center gap-3 rounded-3xl border border-dashed border-slate-200 px-6 py-10 text-center text-slate-500 dark:border-slate-700 dark:text-slate-400',
      className,
    )}
  >
    {icon}
    <p className="text-lg font-semibold text-slate-800 dark:text-slate-100">{title}</p>
    {description && <p className="max-w-sm text-sm text-slate-500 dark:text-slate-400">{description}</p>}
    {actionLabel && onAction && (
      <Button variant="ghost" onClick={onAction}>
        {actionLabel}
      </Button>
    )}
  </div>
)
