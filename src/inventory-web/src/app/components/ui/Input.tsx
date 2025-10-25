import type { InputHTMLAttributes } from 'react'
import { forwardRef, useId } from 'react'
import clsx from 'clsx'

type InputProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'id'> & {
  id?: string
  name: string
  label?: string
  helpText?: string
  containerClassName?: string
}

export const Input = forwardRef<HTMLInputElement, InputProps>(
  ({ id, name, label, helpText, className, containerClassName, ...rest }, ref) => {
    const autoId = useId()
    const finalId = id ?? `${name}-${autoId}`
    return (
      <div className={clsx('flex flex-col gap-2 text-sm', containerClassName)}>
        {label && (
          <label
            htmlFor={finalId}
            className="font-medium text-(--cb-muted) transition-colors duration-200"
          >
            {label}
          </label>
        )}
        <input
          id={finalId}
          name={name}
          className={clsx(
            'h-12 w-full rounded-2xl border border-(--cb-border-soft) bg-(--cb-surface-soft) px-4 text-base text-(--cb-text) shadow-inner transition-all duration-200 [&::placeholder]:text-(--cb-muted) focus-visible:border-brand-300 focus-visible:bg-(--cb-surface) focus-visible:outline-none focus-visible:ring-4 focus-visible:ring-brand-200/50 dark:border-(--cb-border-soft)',
            className,
          )}
          ref={ref}
          {...rest}
        />
        {helpText && <p className="text-xs text-(--cb-muted)">{helpText}</p>}
      </div>
    )
  },
)
Input.displayName = 'Input'
