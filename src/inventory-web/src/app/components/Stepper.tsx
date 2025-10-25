import { clsx } from 'clsx'

interface StepperProps {
  steps: string[]
  activeIndex: number
}

export const Stepper = ({ steps, activeIndex }: StepperProps) => (
  <ol className="flex w-full items-center gap-2 text-xs text-slate-500 dark:text-slate-400 sm:text-sm">
    {steps.map((step, index) => {
      const isActive = index === activeIndex
      const isCompleted = index < activeIndex
      return (
        <li key={step} className="flex flex-1 items-center gap-2">
          <span
            className={clsx(
              'grid h-8 w-8 place-items-center rounded-full border text-sm font-semibold transition-colors',
              isCompleted && 'border-brand-400 bg-brand-500 text-white dark:text-white',
              isActive && !isCompleted &&
                'border-brand-300 bg-brand-100 text-brand-700 dark:bg-brand-500/30 dark:text-brand-100',
              !isActive && !isCompleted &&
                'border-slate-300 bg-white text-slate-500 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-400',
            )}
          >
            {index + 1}
          </span>
          <span className={clsx('font-medium', isActive ? 'text-slate-900 dark:text-slate-100' : '')}>{step}</span>
          {index < steps.length - 1 && (
            <span className="flex-1 border-t border-dashed border-slate-300 dark:border-slate-700" />
          )}
        </li>
      )
    })}
  </ol>
)
