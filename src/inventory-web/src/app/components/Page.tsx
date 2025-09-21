import type { HTMLAttributes } from 'react'
import clsx from 'clsx'

export const Page = ({ className, ...props }: HTMLAttributes<HTMLDivElement>) => (
  <div
    className={clsx(
      'mx-auto flex min-h-screen w-full max-w-3xl flex-col gap-6 bg-gradient-to-b from-slate-950 via-slate-900 to-slate-950 px-4 py-8 text-slate-100',
      className,
    )}
    {...props}
  />
)
