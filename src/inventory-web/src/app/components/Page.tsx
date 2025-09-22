import type { HTMLAttributes } from 'react'
import clsx from 'clsx'
import { ThemeToggle } from './ThemeToggle'

export const Page = ({ className, children, ...props }: HTMLAttributes<HTMLDivElement>) => (
  <div
    className={clsx(
      'mx-auto flex min-h-screen w-full max-w-3xl flex-col gap-6 bg-slate-50 px-4 py-8 text-slate-900 dark:bg-gradient-to-b dark:from-slate-950 dark:via-slate-900 dark:to-slate-950 dark:text-slate-100',
      className,
    )}
    {...props}
  >
    <header className="flex justify-end">
      <ThemeToggle />
    </header>
    {children}
  </div>
)
