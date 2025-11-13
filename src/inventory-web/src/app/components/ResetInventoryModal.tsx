import { useCallback, type KeyboardEvent, type MouseEvent } from 'react'

import { modalContainerStyle } from '@/app/components/Modal/modalContainerStyle'
import { modalOverlayClassName } from '@/app/components/Modal/modalOverlayClassName'
import { ModalPortal } from '@/app/components/Modal/ModalPortal'
import { Button } from '@/app/components/ui/Button'

export interface ResetInventoryModalProps {
  open: boolean
  loading: boolean
  error: string | null
  onConfirm: () => void
  onCancel: () => void
}

export const ResetInventoryModal = ({
  open,
  loading,
  error,
  onConfirm,
  onCancel,
}: ResetInventoryModalProps) => {
  const handleOverlayClick = useCallback(
    (event: MouseEvent<HTMLDivElement>) => {
      if (event.target === event.currentTarget) {
        onCancel()
      }
    },
    [onCancel],
  )

  const handleOverlayKeyDown = useCallback(
    (event: KeyboardEvent<HTMLDivElement>) => {
      if (event.key === 'Escape' || event.key === 'Enter' || event.key === ' ') {
        event.preventDefault()
        onCancel()
      }
    },
    [onCancel],
  )

  if (!open) {
    return null
  }

  return (
    <ModalPortal>
      <div
        className={modalOverlayClassName}
        role="presentation"
        tabIndex={-1}
        onClick={handleOverlayClick}
        onKeyDown={handleOverlayKeyDown}
      >
        <div
          role="dialog"
          aria-modal="true"
          aria-labelledby="reset-inventory-title"
          className="relative flex w-full max-w-lg flex-col gap-4 overflow-y-auto rounded-3xl bg-white p-6 text-left shadow-2xl outline-none focus-visible:ring-2 focus-visible:ring-brand-500 dark:bg-slate-900"
          data-modal-container=""
          style={modalContainerStyle}
          tabIndex={-1}
        >
          <button
            type="button"
            onClick={onCancel}
            className="absolute right-4 top-4 rounded-full border border-slate-200 p-1 text-slate-500 transition hover:bg-slate-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500 focus-visible:ring-offset-2 dark:border-slate-700 dark:text-slate-300 dark:hover:bg-slate-800"
            aria-label="Fermer la confirmation de reset"
          >
            <span aria-hidden="true">✕</span>
          </button>
          <div className="space-y-3">
            <p className="text-xs font-semibold uppercase tracking-[0.3em] text-brand-600 dark:text-brand-200">
              Confirmation
            </p>
            <h2
              id="reset-inventory-title"
              className="text-2xl font-semibold text-slate-900 dark:text-white"
            >
              Relancer un inventaire
            </h2>
            <p className="text-sm text-slate-600 dark:text-slate-300">
              Cette action supprimera tous les comptages, lignes et conflits de l’inventaire en cours pour cette
              boutique. Cette opération est irréversible. Confirmez-vous le reset&nbsp;?
            </p>
            {error && (
              <p className="rounded-xl border border-rose-200 bg-rose-50 px-3 py-2 text-sm text-rose-700 dark:border-rose-500/40 dark:bg-rose-500/10 dark:text-rose-200">
                {error}
              </p>
            )}
          </div>
          <div className="mt-4 flex flex-col gap-3 sm:flex-row sm:justify-end">
            <Button variant="secondary" onClick={onCancel} disabled={loading}>
              Annuler
            </Button>
            <Button variant="danger" onClick={onConfirm} disabled={loading}>
              {loading ? 'Réinitialisation...' : 'Confirmer le reset'}
            </Button>
          </div>
        </div>
      </div>
    </ModalPortal>
  )
}
