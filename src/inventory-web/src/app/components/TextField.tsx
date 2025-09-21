import type { InputHTMLAttributes } from 'react'
import { forwardRef } from 'react'
import clsx from 'clsx'

interface TextFieldProps extends InputHTMLAttributes<HTMLInputElement> {
  label?: string
  helperText?: string
}

export const TextField = forwardRef<HTMLInputElement, TextFieldProps>(
  ({ label, helperText, className, id, ...props }, ref) => {
    const inputId = id ?? props.name
    return (
      <label className="flex w-full flex-col gap-2 text-slate-800 dark:text-slate-200" htmlFor={inputId}>
        {label && <span className="text-sm font-medium text-slate-600 dark:text-slate-300">{label}</span>}
        <input
          ref={ref}
          id={inputId}
          className={clsx(
            'w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-base text-slate-900 placeholder:text-slate-400 focus:border-brand-400 focus:outline-none focus:ring-2 focus:ring-brand-300 dark:border-slate-700 dark:bg-slate-900/60 dark:text-slate-100 dark:placeholder:text-slate-500',
            className,
          )}
          {...props}
        />
        {helperText && <span className="text-xs text-slate-500 dark:text-slate-400">{helperText}</span>}
      </label>
    )
  },
)

TextField.displayName = 'TextField'
