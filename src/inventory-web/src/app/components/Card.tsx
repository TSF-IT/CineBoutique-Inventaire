import type { HTMLAttributes } from 'react'
import clsx from 'clsx'

export const Card = ({ className, ...props }: HTMLAttributes<HTMLDivElement>) => (
  <div
    className={clsx(
      'rounded-3xl bg-white p-6 text-slate-900 shadow-soft ring-1 ring-slate-200/70 backdrop-blur-sm dark:bg-slate-900/70 dark:text-slate-100 dark:ring-white/5',
      className,
    )}
    {...props}
  />
)
