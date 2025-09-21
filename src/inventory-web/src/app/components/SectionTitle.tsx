import type { HTMLAttributes } from 'react'
import clsx from 'clsx'

export const SectionTitle = ({ className, ...props }: HTMLAttributes<HTMLHeadingElement>) => (
  <h2
    className={clsx('text-xl font-semibold text-slate-100 sm:text-2xl', className)}
    {...props}
  />
)
