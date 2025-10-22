import type { HTMLAttributes } from 'react'
import clsx from 'clsx'

export const Card = ({ className, ...props }: HTMLAttributes<HTMLDivElement>) => (
  <div
    className={clsx(
      'rounded-2xl border border-[var(--stroke)] bg-[var(--surface)] p-6 text-[var(--text-strong)] shadow-elev-1',
      className,
    )}
    {...props}
  />
)
