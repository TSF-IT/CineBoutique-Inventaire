import type { HTMLAttributes, ReactNode } from 'react'
import clsx from 'clsx'

type PageShellProps = HTMLAttributes<HTMLDivElement> & {
  header: ReactNode
  nav?: ReactNode
  mainClassName?: string
  children: ReactNode
}

export const PageShell = ({
  header,
  nav,
  children,
  className,
  mainClassName,
  style,
  ...rest
}: PageShellProps) => (
  <div
    className={clsx('page-shell safe-pads', className)}
    style={{
      minHeight: '100dvh',
      display: 'grid',
      gridTemplateRows: nav ? 'auto 1fr auto' : 'auto 1fr',
      gap: 'var(--spacing-3)',
      ...(style ?? {}),
    }}
    {...rest}
  >
    <header className="page-shell__header">{header}</header>
    <main className={clsx('layout-main container', mainClassName)}>{children}</main>
    {nav ? (
      <nav className="mobile-bottom-bar" aria-label="Actions principales">
        {nav}
      </nav>
    ) : null}
  </div>
)
