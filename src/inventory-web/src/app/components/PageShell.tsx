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
    className={clsx('page-shell safe-pads', nav && 'page-shell--with-nav', className)}
    style={style}
    {...rest}
  >
    <header className="page-shell__header">
      <div className="container">
        <div className="page-shell__header-surface">
          <div className="page-shell__header-inner">{header}</div>
        </div>
      </div>
    </header>
    <main className={clsx('layout-main container', mainClassName)}>{children}</main>
    {nav ? (
      <nav className="mobile-bottom-bar" aria-label="Actions principales">
        {nav}
      </nav>
    ) : null}
  </div>
)
