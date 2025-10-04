import clsx from 'clsx'

interface StepperProps {
  steps: string[]
  activeIndex: number
}

export const Stepper = ({ steps, activeIndex }: StepperProps) => (
  <ol
    className="grid w-full grid-cols-2 gap-2 px-2 py-1 text-[clamp(11px,2.6vw,14px)] leading-snug text-slate-500 dark:text-slate-400 sm:flex sm:items-center sm:gap-2 sm:px-0 sm:py-0 sm:text-sm"
    aria-label="Assistant d’inventaire"
  >
    {steps.map((step, index) => {
      const isActive = index === activeIndex
      const isCompleted = index < activeIndex
      return (
        <li
          key={step}
          aria-label={`Étape ${index + 1}: ${step}`}
          aria-current={isActive ? 'step' : undefined}
          className="flex min-w-[88px] items-center gap-2 rounded-xl px-2 py-1 sm:min-w-0 sm:flex-1 sm:rounded-none sm:px-0 sm:py-0"
        >
          <span
            className={clsx(
              'grid h-5 w-5 place-items-center rounded-full border text-[11px] font-semibold transition-colors sm:h-8 sm:w-8 sm:text-sm',
              isCompleted && 'border-brand-400 bg-brand-500 text-white dark:text-white',
              isActive && !isCompleted &&
                'border-brand-300 bg-brand-100 text-brand-700 dark:bg-brand-500/30 dark:text-brand-100',
              !isActive && !isCompleted &&
                'border-slate-300 bg-white text-slate-500 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-400',
            )}
          >
            {index + 1}
          </span>
          <span
            className={clsx(
              'break-words font-medium text-slate-500 dark:text-slate-400',
              isActive && 'text-slate-900 dark:text-slate-100',
            )}
            title={step}
          >
            {step}
          </span>
          {index < steps.length - 1 && (
            <span className="hidden flex-1 border-t border-dashed border-slate-300 dark:border-slate-700 sm:block" />
          )}
        </li>
      )
    })}
  </ol>
)
