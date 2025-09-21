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
    'bg-brand-500 text-white shadow-soft hover:bg-brand-400 active:bg-brand-600 focus-visible:ring-2 focus-visible:ring-brand-300',
  secondary:
    'bg-slate-800 text-slate-50 hover:bg-slate-700 active:bg-slate-900 focus-visible:ring-2 focus-visible:ring-brand-300',
  ghost:
    'bg-transparent text-brand-200 hover:bg-slate-800 focus-visible:ring-2 focus-visible:ring-brand-300',
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
