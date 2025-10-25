import { clsx } from 'clsx'
import { Link } from 'react-router-dom'
import type { LinkProps } from 'react-router-dom'

type BackToShopSelectionLinkProps = {
  label?: string
  state?: LinkProps['state']
  onClick?: LinkProps['onClick']
  className?: string
  to?: LinkProps['to']
}

const baseClasses =
  'inline-flex h-11 w-11 min-h-[var(--tap-min)] items-center justify-center rounded-full border border-(--cb-border-soft) bg-(--cb-surface-soft) text-lg font-semibold text-brand-600 shadow-panel-soft transition-all duration-200 hover:-translate-y-0.5 hover:text-brand-500 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-300 focus-visible:ring-offset-2 focus-visible:ring-offset-(--cb-surface)'

export const BackToShopSelectionLink = ({
  label = "Retour au choix de l’entité",
  state,
  onClick,
  className,
  to = '/select-shop',
}: BackToShopSelectionLinkProps) => (
  <Link
    to={to}
    state={state}
    onClick={onClick}
    className={clsx(baseClasses, className)}
    aria-label={label}
  >
    <span aria-hidden="true">←</span>
    <span className="sr-only">{label}</span>
  </Link>
)
