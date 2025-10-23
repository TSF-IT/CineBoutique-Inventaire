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
      'page-content cb-surface-panel flex w-full flex-col gap-6 px-4 py-6 text-base sm:px-8 sm:py-10',
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
                className="inline-flex items-center justify-center gap-2 rounded-2xl border border-[var(--cb-border-strong)] bg-[var(--cb-surface-soft)] px-3 py-2 text-sm font-semibold text-brand-600 shadow-sm transition hover:brightness-110 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-400 focus-visible:ring-offset-2 focus-visible:ring-offset-[var(--cb-surface-strong)] dark:text-brand-200"
                data-testid="btn-go-home"
                aria-label="Revenir à l’accueil"
              >
                <span aria-hidden="true">←</span>
                <span className="sr-only">{homeLinkLabel}</span>
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
