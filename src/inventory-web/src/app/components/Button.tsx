import type { ButtonHTMLAttributes } from 'react'
import { forwardRef } from 'react'
import clsx from 'clsx'

type ButtonVariant = 'primary' | 'secondary' | 'ghost'

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant
  fullWidth?: boolean
}

const variantStyles: Record<ButtonVariant, string> = {
  primary:
    'bg-brand-600 text-white shadow-soft hover:bg-brand-500 active:bg-brand-700 focus-visible:ring-2 focus-visible:ring-brand-300 dark:bg-brand-500 dark:hover:bg-brand-400',
  secondary:
    'bg-slate-200 text-slate-900 hover:bg-slate-300 active:bg-slate-400 focus-visible:ring-2 focus-visible:ring-brand-300 dark:bg-slate-800 dark:text-slate-100 dark:hover:bg-slate-700',
  ghost:
    'bg-transparent text-brand-600 hover:bg-slate-100 focus-visible:ring-2 focus-visible:ring-brand-300 dark:text-brand-200 dark:hover:bg-slate-800/60',
}

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant = 'primary', fullWidth = false, ...props }, ref) => (
    <button
      ref={ref}
      className={clsx(
        'rounded-2xl px-4 py-3 text-base font-semibold transition-all duration-150 focus:outline-none',
        fullWidth && 'w-full',
        variantStyles[variant],
        className,
      )}
      {...props}
    />
  ),
)

Button.displayName = 'Button'
