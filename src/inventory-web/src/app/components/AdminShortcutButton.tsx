import { clsx } from 'clsx'
import { Link } from 'react-router-dom'

type AdminShortcutButtonProps = {
  className?: string
}

export const AdminShortcutButton = ({ className }: AdminShortcutButtonProps) => (
  <Link
    to="/admin"
    className={clsx(
      'inline-flex h-11 w-11 min-h-(--tap-min) items-center justify-center rounded-full border border-(--cb-border-soft) bg-(--cb-surface-soft) text-lg font-semibold text-brand-600 shadow-panel-soft transition-all duration-200 hover:-translate-y-0.5 hover:text-brand-500 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-300 focus-visible:ring-offset-2 focus-visible:ring-offset-(--cb-surface) dark:text-brand-200 dark:hover:text-brand-100',
      className,
    )}
    aria-label="Accéder à l’espace administrateur"
  >
    <span aria-hidden="true">⚙️</span>
    <span className="sr-only">Accéder à l’espace administrateur</span>
  </Link>
)
