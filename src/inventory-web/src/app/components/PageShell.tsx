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
}: PageShellProps) => {
  const hasNav = Boolean(nav)

  return (
    <div
      className={clsx(
        'page-shell safe-pads max-w-full overflow-visible bg-gray-50 text-gray-900 antialiased',
        hasNav && 'page-shell--with-nav',
        className,
      )}
      style={style}
      {...rest}
    >
      <main
        className={clsx(
          'layout-main cb-container overflow-visible',
          hasNav ? 'pb-24' : 'pb-12',
          mainClassName,
        )}
      >
        {header ?? null}
        {children}
      </main>
      {nav ? (
        <nav className="mobile-bottom-bar" aria-label="Actions principales">
          {nav}
        </nav>
      ) : null}
    </div>
  )
}
