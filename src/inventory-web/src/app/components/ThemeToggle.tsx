import { useMemo } from 'react'
import clsx from 'clsx'
import { useTheme } from '../contexts/ThemeContext'

export const ThemeToggle = () => {
  const { theme, toggleTheme } = useTheme()
  const isDark = theme === 'dark'

  const label = useMemo(
    () => (isDark ? 'Basculer en thème clair' : 'Basculer en thème sombre'),
    [isDark],
  )

  return (
    <button
      type="button"
      onClick={toggleTheme}
      aria-label={label}
      className={clsx(
        'inline-flex items-center gap-2 rounded-full border px-3 py-2 text-sm font-medium transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-400',
        'border-slate-300 bg-white text-slate-700 hover:bg-slate-100 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-100 dark:hover:bg-slate-800',
      )}
    >
      <span aria-hidden>{isDark ? '🌙' : '☀️'}</span>
      <span>{isDark ? 'Sombre' : 'Clair'}</span>
    </button>
  )
}
