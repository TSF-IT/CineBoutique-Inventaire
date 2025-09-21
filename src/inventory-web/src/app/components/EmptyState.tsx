import type { ReactNode } from 'react'
import clsx from 'clsx'

interface EmptyStateProps {
  icon?: ReactNode
  title: string
  description?: string
  className?: string
}

export const EmptyState = ({ icon, title, description, className }: EmptyStateProps) => (
  <div
    className={clsx(
      'flex flex-col items-center justify-center gap-3 rounded-3xl border border-dashed border-slate-700 px-6 py-10 text-center text-slate-400',
      className,
    )}
  >
    {icon}
    <p className="text-lg font-semibold text-slate-200">{title}</p>
    {description && <p className="max-w-sm text-sm text-slate-400">{description}</p>}
  </div>
)
