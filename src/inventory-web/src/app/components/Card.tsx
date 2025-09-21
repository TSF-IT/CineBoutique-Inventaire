import type { HTMLAttributes } from 'react'
import clsx from 'clsx'

export const Card = ({ className, ...props }: HTMLAttributes<HTMLDivElement>) => (
  <div
    className={clsx(
      'rounded-3xl bg-slate-900/70 p-6 backdrop-blur-sm ring-1 ring-white/5 shadow-soft',
      className,
    )}
    {...props}
  />
)
