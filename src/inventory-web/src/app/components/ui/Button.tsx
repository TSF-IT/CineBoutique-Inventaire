import { clsx } from 'clsx'
import type { ButtonHTMLAttributes } from 'react'
import { forwardRef } from 'react'

type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'outline' | 'danger'
type ButtonSize = 'sm' | 'md' | 'lg'

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: ButtonVariant
  size?: ButtonSize
  className?: string
  fullWidth?: boolean
}

const baseClasses =
  'inline-flex items-center justify-center gap-2 rounded-full font-semibold tracking-[-0.01em] transition-all duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus focus-visible:ring-offset-2 focus-visible:ring-offset-(--cb-surface-soft) disabled:cursor-not-allowed disabled:opacity-60 disabled:shadow-none'

const sizeClasses: Record<ButtonSize, string> = {
  sm: 'h-10 px-3 text-sm',
  md: 'h-12 px-5 text-base',
  lg: 'h-14 px-6 text-lg',
}

const variantClasses: Record<ButtonVariant, string> = {
  primary:
    'bg-brand-600 text-white shadow-soft hover:-translate-y-0.5 hover:bg-brand-500 focus-visible:ring-brand-300 focus-visible:ring-offset-(--cb-surface) active:translate-y-0 dark:bg-brand-500 dark:hover:bg-brand-400',
  secondary:
    'bg-(--cb-surface-soft) text-(--cb-text) border border-(--cb-border-soft) shadow-panel-soft hover:-translate-y-0.5 hover:bg-(--cb-surface) focus-visible:ring-brand-200 active:translate-y-0',
  outline:
    'border border-(--cb-border-strong) bg-transparent text-(--cb-text) shadow-none hover:bg-(--cb-surface-soft) focus-visible:ring-brand-200',
  ghost:
    'bg-transparent text-(--cb-text) shadow-none hover:bg-(--cb-surface-soft) hover:text-brand-600 focus-visible:ring-transparent dark:hover:text-brand-200',
  danger:
    'bg-red-600 text-white shadow-soft hover:-translate-y-0.5 hover:bg-red-500 focus-visible:ring-red-300 focus-visible:ring-offset-(--cb-surface) active:translate-y-0 dark:bg-red-500 dark:hover:bg-red-400',
}

/**
 * Bouton avec type par défaut = "button" pour éviter les submits involontaires.
 * On peut override via props.type = "submit" si nécessaire.
 */
export const Button = forwardRef<HTMLButtonElement, ButtonProps>(
  ({ variant = 'primary', size = 'md', className, fullWidth = false, type, ...props }, ref) => {
    const composedClassName = clsx(
      baseClasses,
      sizeClasses[size],
      variantClasses[variant],
      'min-h-[var(--tap-min)]',
      fullWidth && 'w-full',
      className,
    )

    return (
      <button
        ref={ref}
        className={composedClassName}
        type={type === 'submit' ? 'submit' : type === 'reset' ? 'reset' : 'button'}
        {...props}
      />
    )
  },
)
Button.displayName = 'Button'

