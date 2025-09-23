import { Button } from './Button'

interface ErrorPanelProps {
  title: string
  details?: string
  actionLabel?: string
  onAction?: () => void
}

export const ErrorPanel = ({ title, details, actionLabel, onAction }: ErrorPanelProps) => {
  return (
    <div
      role="alert"
      className="rounded-3xl border border-red-200 bg-red-50 p-5 text-left text-red-700 shadow-sm dark:border-red-500/40 dark:bg-red-500/10 dark:text-red-200"
    >
      <p className="text-base font-semibold">{title}</p>
      {details && <p className="mt-2 whitespace-pre-line text-sm leading-relaxed">{details}</p>}
      {actionLabel && onAction && (
        <Button className="mt-4" variant="secondary" onClick={onAction}>
          {actionLabel}
        </Button>
      )}
    </div>
  )
}
