import type { HTMLAttributes, ReactNode } from 'react'
import clsx from 'clsx'
import { Link } from 'react-router-dom'
import { ThemeToggle } from './ThemeToggle'

type PageProps = HTMLAttributes<HTMLDivElement> & {
  showHomeLink?: boolean
  homeLinkLabel?: string
  headerAction?: ReactNode
}

export const Page = ({
  className,
  children,
  showHomeLink = false,
  homeLinkLabel = 'Retour à l’accueil',
  headerAction,
  ...props
}: PageProps) => (
  <div
    className={clsx(
      'mx-auto flex min-h-screen w-full max-w-3xl flex-col gap-6 bg-slate-50 px-4 py-8 text-slate-900 dark:bg-gradient-to-b dark:from-slate-950 dark:via-slate-900 dark:to-slate-950 dark:text-slate-100',
      className,
    )}
    {...props}
  >
    <header
      className={clsx(
        'flex items-center',
        showHomeLink || headerAction ? 'justify-between gap-3' : 'justify-end',
      )}
    >
      {(showHomeLink || headerAction) && (
        <div className="flex items-center gap-3">
          {showHomeLink && (
            <Link
              to="/"
              className="inline-flex items-center gap-2 rounded-2xl border border-transparent bg-white px-3 py-2 text-sm font-semibold text-brand-600 shadow-sm transition hover:bg-brand-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500 dark:bg-slate-900/60 dark:text-brand-200 dark:hover:bg-slate-800"
              data-testid="btn-go-home"
              aria-label="Revenir à l’accueil"
            >
              <span aria-hidden="true">←</span>
              <span>{homeLinkLabel}</span>
            </Link>
          )}
          {headerAction}
        </div>
      )}
      <ThemeToggle />
    </header>
    {children}
  </div>
)
