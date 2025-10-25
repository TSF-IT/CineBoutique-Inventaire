import type { HTMLAttributes } from 'react'
import clsx from 'clsx'

type CardProps = HTMLAttributes<HTMLDivElement> & {
  padding?: 'tight' | 'default' | 'relaxed'
  elevated?: boolean
}

const paddingMap: Record<NonNullable<CardProps['padding']>, string> = {
  tight: 'p-4 sm:p-5',
  default: 'p-6 sm:p-8',
  relaxed: 'p-6 sm:p-10',
}

export const Card = ({
  className,
  padding = 'default',
  elevated = false,
  ...props
}: CardProps) => (
  <div
    className={clsx(
      'relative rounded-3xl border border-(--cb-border-soft) bg-(--cb-surface) text-(--cb-text) shadow-panel-soft transition-colors duration-200',
      elevated && 'shadow-panel',
      paddingMap[padding],
      className,
    )}
    {...props}
  />
)
