import clsx from 'clsx'
import { Link } from 'react-router-dom'
import type { LinkProps } from 'react-router-dom'

type BackToShopSelectionLinkProps = {
  label?: string
  to?: LinkProps['to']
  state?: LinkProps['state']
  onClick?: LinkProps['onClick']
  className?: string
}

const baseClasses =
  'inline-flex items-center gap-2 rounded-2xl border border-transparent bg-white px-3 py-2 text-sm font-semibold text-brand-600 shadow-sm transition hover:bg-brand-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500 dark:bg-slate-900/60 dark:text-brand-200 dark:hover:bg-slate-800'

export const BackToShopSelectionLink = ({
  label = "Retour au choix d’utilisateur",
  to = '/select-user',
  state,
  onClick,
  className,
}: BackToShopSelectionLinkProps) => (
  <Link
    to={to}
    state={state}
    onClick={onClick}
    className={clsx(baseClasses, className)}
    aria-label={label}
  >
    <span aria-hidden="true">←</span>
    <span>{label}</span>
  </Link>
)
