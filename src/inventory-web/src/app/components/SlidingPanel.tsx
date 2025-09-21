import type { ReactNode } from 'react'
import clsx from 'clsx'

interface SlidingPanelProps {
  open: boolean
  title: string
  onClose: () => void
  children: ReactNode
}

export const SlidingPanel = ({ open, title, onClose, children }: SlidingPanelProps) => (
  <div
    className={clsx(
      'pointer-events-none fixed inset-0 z-40 flex items-end justify-center bg-black/40 transition-opacity duration-300',
      open ? 'opacity-100' : 'opacity-0',
    )}
    role="dialog"
    aria-modal="true"
    aria-hidden={!open}
  >
    <div
      className={clsx(
        'pointer-events-auto w-full max-w-3xl translate-y-full rounded-t-3xl bg-slate-950/95 p-6 shadow-2xl transition-transform duration-300 backdrop-blur-md',
        open ? 'translate-y-0' : 'translate-y-full',
      )}
    >
      <div className="mb-4 flex items-center justify-between">
        <h3 className="text-lg font-semibold text-slate-100">{title}</h3>
        <button
          type="button"
          className="rounded-full bg-slate-800 px-3 py-1 text-sm font-medium text-slate-200"
          onClick={onClose}
        >
          Fermer
        </button>
      </div>
      <div className="space-y-4">{children}</div>
    </div>
  </div>
)
