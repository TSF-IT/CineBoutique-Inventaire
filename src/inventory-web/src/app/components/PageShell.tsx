import { clsx } from 'clsx'
import type { HTMLAttributes, ReactNode } from 'react'

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
        'page-shell safe-pads relative flex min-h-screen w-full flex-col overflow-visible text-(--cb-text) antialiased transition-colors duration-300',
        hasNav && 'page-shell--with-nav',
        className,
      )}
      style={style}
      {...rest}
    >
      <main
        className={clsx(
          'layout-main cb-container mx-auto flex w-full max-w-[var(--content-max)] flex-col overflow-visible',
          hasNav ? 'pb-[calc(5rem+env(safe-area-inset-bottom))]' : 'pb-16',
          mainClassName,
        )}
      >
        {header ?? null}
        {children}
      </main>
      {nav ? (
        <nav
          className="mobile-bottom-bar border border-(--cb-border-soft) bg-(--cb-surface) text-(--cb-text) shadow-panel-soft"
          aria-label="Actions principales"
        >
          {nav}
        </nav>
      ) : null}
    </div>
  )
}
