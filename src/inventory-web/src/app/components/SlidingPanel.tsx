// Modifications : activation du scroll interne et amélioration de l’overlay du panneau coulissant.
import { clsx } from 'clsx'
import type { ReactNode } from 'react'

interface SlidingPanelProps {
  open: boolean
  title: string
  onClose: () => void
  children: ReactNode
}

export const SlidingPanel = ({ open, title, onClose, children }: SlidingPanelProps) => (
  <div
    className={clsx(
      'fixed inset-0 z-40 flex items-end justify-center bg-black/40 transition-opacity duration-300',
      open ? 'pointer-events-auto opacity-100' : 'pointer-events-none opacity-0',
    )}
    role="dialog"
    aria-modal="true"
    aria-hidden={!open}
  >
    <div
      className={clsx(
        'pointer-events-auto w-full max-w-3xl translate-y-full rounded-t-3xl bg-white shadow-2xl transition-transform duration-300 backdrop-blur-md dark:bg-slate-950/95',
        open ? 'translate-y-0' : 'translate-y-full',
      )}
    >
      <div className="max-h-[80vh] overflow-y-auto p-6">
        <div className="mb-4 flex items-center justify-between">
          <h3 className="text-lg font-semibold text-slate-900 dark:text-slate-100">{title}</h3>
          <button
            type="button"
            className="rounded-full border border-slate-200 px-3 py-1 text-sm font-medium text-slate-700 transition-colors hover:bg-slate-100 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-200 dark:hover:bg-slate-700"
            onClick={onClose}
          >
            Fermer
          </button>
        </div>
        <div className="space-y-4">{children}</div>
      </div>
    </div>
  </div>
)
