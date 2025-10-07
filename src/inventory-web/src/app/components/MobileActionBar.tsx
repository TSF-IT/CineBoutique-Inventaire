import clsx from 'clsx'
import { Button } from './ui/Button'

type ActionConfig = {
  label?: string
  onClick?: () => void
  disabled?: boolean
}

type CompleteActionConfig = ActionConfig & {
  busy?: boolean
}

type MobileActionBarProps = {
  className?: string
  scan?: ActionConfig
  restart?: ActionConfig
  complete?: CompleteActionConfig
}

export const MobileActionBar = ({ className, scan, restart, complete }: MobileActionBarProps) => (
  <div className={clsx('mobile-bottom-bar__content', className)}>
    <Button
      type="button"
      variant="secondary"
      fullWidth
      className="btn"
      disabled={scan?.disabled}
      onClick={scan?.onClick}
    >
      {scan?.label ?? 'Scanner'}
    </Button>
    <Button
      type="button"
      variant="ghost"
      fullWidth
      className="btn"
      disabled={restart?.disabled}
      onClick={restart?.onClick}
    >
      {restart?.label ?? 'Relancer un comptage'}
    </Button>
    <Button
      type="button"
      fullWidth
      className="btn"
      aria-busy={complete?.busy ? 'true' : undefined}
      disabled={complete?.disabled}
      onClick={complete?.onClick}
    >
      {complete?.label ?? (complete?.busy ? 'Enregistrement…' : 'Terminer')}
    </Button>
  </div>
)
