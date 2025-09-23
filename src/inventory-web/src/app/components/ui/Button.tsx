import type { ButtonHTMLAttributes } from 'react'
import { forwardRef } from 'react'
import clsx from 'clsx'

type ButtonVariant = 'primary' | 'secondary' | 'ghost'

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: ButtonVariant
  className?: string
  fullWidth?: boolean
}

/**
 * Bouton avec type par défaut = "button" pour éviter les submits involontaires.
 * On peut override via props.type = "submit" si nécessaire.
 */
export const Button = forwardRef<HTMLButtonElement, ButtonProps>(
  ({ variant = 'primary', className, fullWidth = false, ...props }, ref) => {
    const { type, ...rest } = props
    const base =
      'rounded-2xl px-4 py-3 text-base font-semibold transition-all duration-150 focus:outline-none'
    const variants: Record<ButtonVariant, string> = {
      primary:
        'bg-brand-600 text-white shadow-soft hover:bg-brand-500 active:bg-brand-700 focus-visible:ring-2 focus-visible:ring-brand-300 dark:bg-brand-500 dark:hover:bg-brand-400',
      secondary:
        'bg-gray-200 text-gray-900 hover:bg-gray-300 dark:bg-gray-700 dark:text-white dark:hover:bg-gray-600',
      ghost: 'bg-transparent text-inherit hover:bg-gray-100 dark:hover:bg-gray-800',
    }

    const buttonClassName = clsx(base, fullWidth && 'w-full', variants[variant], className)
    const commonProps = {
      ref,
      className: buttonClassName,
      ...rest,
    }

    if (type === 'submit') {
      return <button {...commonProps} type="submit" />
    }

    if (type === 'reset') {
      return <button {...commonProps} type="reset" />
    }

    return <button {...commonProps} type="button" />
  },
)
Button.displayName = 'Button'
