import { clsx } from 'clsx'
import type { ReactNode } from 'react'

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
      'flex flex-col items-center justify-center gap-4 rounded-3xl border border-dashed border-(--cb-border-soft) bg-(--cb-surface-soft) px-6 py-10 text-center text-(--cb-muted) shadow-panel-soft',
      className,
    )}
  >
    {icon}
    <p className="text-lg font-semibold text-(--cb-text)">{title}</p>
    {description && <p className="max-w-sm text-sm text-(--cb-muted)">{description}</p>}
    {actionLabel && onAction && (
      <Button variant="ghost" onClick={onAction}>
        {actionLabel}
      </Button>
    )}
  </div>
)
