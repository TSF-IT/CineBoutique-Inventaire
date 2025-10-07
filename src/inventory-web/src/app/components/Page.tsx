import type { HTMLAttributes, ReactNode } from 'react'
import clsx from 'clsx'
import { Link } from 'react-router-dom'
import { ThemeToggle } from './ThemeToggle'
import { PageShell } from './PageShell'

type PageProps = HTMLAttributes<HTMLDivElement> & {
  showHomeLink?: boolean
  homeLinkLabel?: string
  headerAction?: ReactNode
  mobileNav?: ReactNode
}

export const Page = ({
  className,
  children,
  showHomeLink = false,
  homeLinkLabel = 'Retour à l’accueil',
  headerAction,
  mobileNav,
  ...rest
}: PageProps) => (
  <PageShell
    {...rest}
    mainClassName={clsx(
      'page-content flex w-full flex-col gap-6 rounded-3xl bg-slate-50 px-4 py-6 text-slate-900 shadow-sm dark:bg-slate-900/70 dark:text-slate-100',
      className,
    )}
    nav={mobileNav}
    header={
      <div className="page-header flex w-full items-center gap-3">
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
        <div className="ml-auto">
          <ThemeToggle />
        </div>
      </div>
    }
  >
    {children}
  </PageShell>
)
