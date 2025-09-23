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
      <div className={clsx('flex flex-col gap-1', containerClassName)}>
        {label && (
          <label htmlFor={finalId} className="text-sm font-medium text-gray-700 dark:text-gray-200">
            {label}
          </label>
        )}
        <input
          id={finalId}
          name={name}
          className={clsx(
            'rounded-xl border border-gray-300 px-3 py-2 text-base dark:bg-gray-800 dark:text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-300',
            className,
          )}
          ref={ref}
          {...rest}
        />
        {helpText && <p className="text-xs text-gray-500 dark:text-gray-400">{helpText}</p>}
      </div>
    )
  },
)
Input.displayName = 'Input'
