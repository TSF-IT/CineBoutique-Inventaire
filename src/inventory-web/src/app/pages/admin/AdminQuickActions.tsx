import { clsx } from 'clsx'
import { Fragment, useEffect, useMemo, useState } from 'react'
import { Link, useLocation } from 'react-router-dom'

type QuickAction = {
  to: string
  label: string
  description: string
}

const ACTIONS: QuickAction[] = [
  { to: '/admin', label: 'Zones', description: 'Gérer les emplacements et zones de comptage' },
  { to: '/admin/products', label: 'Produits', description: 'Consulter et ajuster le catalogue' },
  { to: '/admin/import', label: 'Import', description: 'Importer une liste de produits' },
]

export const AdminQuickActions = () => {
  const location = useLocation()
  const [open, setOpen] = useState(false)

  useEffect(() => {
    if (!open) {
      return
    }
    const handler = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setOpen(false)
      }
    }
    window.addEventListener('keydown', handler)
    return () => window.removeEventListener('keydown', handler)
  }, [open])

  const activePath = useMemo(() => {
    const pathname = location.pathname ?? ''
    if (pathname.startsWith('/admin/import')) {
      return '/admin/import'
    }
    if (pathname.startsWith('/admin/products')) {
      return '/admin/products'
    }
    return '/admin'
  }, [location.pathname])

  return (
    <Fragment>
      {open ? (
        <button
          type="button"
          aria-label="Fermer le menu d’actions"
          onClick={() => setOpen(false)}
          className="fixed inset-0 z-40 bg-transparent md:hidden"
        />
      ) : null}
      <div className="fixed bottom-[calc(1.5rem+env(safe-area-inset-bottom))] right-[calc(1.25rem+env(safe-area-inset-right))] z-50 md:hidden">
        <div
          className={clsx(
            'origin-bottom-right transition-all duration-200',
            open ? 'scale-100 opacity-100' : 'scale-95 opacity-0 pointer-events-none',
          )}
          aria-hidden={!open}
        >
          <div className="mb-3 w-[min(280px,80vw)] rounded-3xl border border-(--cb-border-soft) bg-(--cb-surface) p-3 shadow-panel">
            <p className="px-2 text-xs font-semibold uppercase tracking-[0.28em] text-(--cb-muted)">
              Navigation admin
            </p>
            <ul className="mt-2 space-y-2">
              {ACTIONS.map((action) => {
                const isActive = activePath === action.to
                return (
                  <li key={action.to}>
                    <Link
                      to={action.to}
                      onClick={() => setOpen(false)}
                      className={clsx(
                        'flex flex-col rounded-2xl border px-3 py-2 transition-all duration-150',
                        isActive
                          ? 'border-brand-500/50 bg-brand-500/10 text-brand-700'
                          : 'border-(--cb-border-soft) bg-(--cb-surface-soft) text-(--cb-text) hover:border-brand-400/60 hover:bg-brand-500/10',
                      )}
                    >
                      <span className="text-sm font-semibold">{action.label}</span>
                      <span className="text-xs text-(--cb-muted)">{action.description}</span>
                    </Link>
                  </li>
                )
              })}
            </ul>
          </div>
        </div>
        <button
          type="button"
          className="inline-flex items-center justify-center rounded-full bg-brand-600 text-white shadow-fab transition hover:bg-brand-500 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-300 focus-visible:ring-offset-2 focus-visible:ring-offset-(--cb-surface)"
          onClick={() => setOpen((previous) => !previous)}
          aria-label="Actions administrateur"
          aria-expanded={open}
          style={{ width: '3.5rem', height: '3.5rem' }}
        >
          {open ? '×' : '≡'}
        </button>
      </div>
    </Fragment>
  )
}
