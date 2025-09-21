import clsx from 'clsx'

interface StepperProps {
  steps: string[]
  activeIndex: number
}

export const Stepper = ({ steps, activeIndex }: StepperProps) => (
  <ol className="flex w-full items-center gap-2 text-xs text-slate-400 sm:text-sm">
    {steps.map((step, index) => {
      const isActive = index === activeIndex
      const isCompleted = index < activeIndex
      return (
        <li key={step} className="flex flex-1 items-center gap-2">
          <span
            className={clsx(
              'grid h-8 w-8 place-items-center rounded-full border text-sm font-semibold transition-colors',
              isCompleted && 'border-brand-400 bg-brand-500 text-white',
              isActive && !isCompleted && 'border-brand-300 bg-brand-500/30 text-brand-100',
              !isActive && !isCompleted && 'border-slate-700 bg-slate-900 text-slate-500',
            )}
          >
            {index + 1}
          </span>
          <span className={clsx('font-medium', isActive ? 'text-slate-100' : 'text-slate-500')}>{step}</span>
          {index < steps.length - 1 && <span className="flex-1 border-t border-dashed border-slate-700" />}
        </li>
      )
    })}
  </ol>
)
