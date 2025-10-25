import { clsx } from 'clsx'
import type { HTMLAttributes, PropsWithChildren } from 'react'

export const SectionTitle = ({ className, children, ...props }: PropsWithChildren<HTMLAttributes<HTMLHeadingElement>>) => (
  <h2
    className={clsx('text-xl font-semibold text-slate-900 dark:text-slate-100 sm:text-2xl', className)}
    {...props}
  >
    {children}
  </h2>
)
