import clsx from 'clsx';

const STEP_LABELS = ['Zone', 'Type de comptage', 'Scan'] as const;

interface StepperProps {
  activeIndex: number;
}

export function Stepper({ activeIndex }: StepperProps) {
  const currentIndex = Math.min(activeIndex, STEP_LABELS.length - 1);

  return (
    <div className="grid grid-cols-3 [@media(orientation:landscape)]:grid-cols-3 sm:grid-cols-3 gap-2 sm:gap-3 px-2 py-1 sm:px-4 sm:py-3">
      {STEP_LABELS.map((label, index) => {
        const isActive = index === currentIndex;
        const isCompleted = index < currentIndex;

        return (
          <div
            key={label}
            data-step-index={index}
            className={clsx(
              'min-w-[88px] rounded-xl border px-2 py-1 sm:px-3 sm:py-2 transition-colors',
              isCompleted && 'border-brand-400 bg-brand-500/10 dark:border-brand-500',
              isActive && !isCompleted && 'border-brand-300 bg-brand-100/70 dark:border-brand-500/70',
            )}
          >
            <div className="flex items-center gap-2">
              <span
                className={clsx(
                  'inline-grid h-5 w-5 place-items-center rounded-full border text-[11px] font-semibold sm:h-6 sm:w-6 sm:text-[12px]',
                  isCompleted &&
                    'border-brand-400 bg-brand-500 text-white dark:border-brand-400 dark:bg-brand-500 dark:text-white',
                  isActive &&
                    !isCompleted &&
                    'border-brand-300 bg-brand-100 text-brand-700 dark:border-brand-500 dark:bg-brand-500/40 dark:text-brand-50',
                  !isActive &&
                    !isCompleted &&
                    'border-slate-300 bg-white text-slate-500 dark:border-slate-600 dark:bg-slate-900 dark:text-slate-400',
                )}
              >
                {index + 1}
              </span>
              <span
                className={clsx(
                  'text-[clamp(11px,2.6vw,14px)] leading-snug break-words',
                  isActive && 'font-medium text-slate-900 dark:text-slate-100',
                )}
              >
                {label}
              </span>
            </div>
          </div>
        );
      })}
    </div>
  );
}
