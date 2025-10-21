import type { HTMLAttributes } from 'react'
import clsx from 'clsx'

export const Card = ({ className, ...props }: HTMLAttributes<HTMLDivElement>) => (
  <div
    className={clsx(
      'cb-card p-6',
      className,
    )}
    {...props}
  />
)
