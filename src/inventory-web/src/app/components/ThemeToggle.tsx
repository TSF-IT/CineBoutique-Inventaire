import { useMemo } from 'react'
import clsx from 'clsx'
import { useTheme } from '../../theme/ThemeProvider'

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
      role="switch"
      aria-label={label}
      aria-checked={isDark}
      title={`Thème: ${isDark ? 'sombre' : 'clair'}`}
      className={clsx(
        'group relative inline-flex h-11 w-24 items-center overflow-hidden rounded-full border border-[var(--cb-border-strong)] bg-[var(--cb-surface-soft)] px-1 text-xs font-semibold uppercase tracking-wide text-[var(--cb-muted)] transition-all duration-200 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-400 focus-visible:ring-offset-2 focus-visible:ring-offset-[var(--cb-surface-strong)]',
        isDark ? 'shadow-[var(--cb-card-shadow-soft)]' : 'shadow-sm',
      )}
      style={{ minHeight: 'var(--tap-min)' }}
    >
      <span className="sr-only">{label}</span>
      <span
        aria-hidden
        className={clsx(
          'pointer-events-none absolute inset-0 rounded-full transition-all duration-300 ease-out',
          isDark ? 'bg-[var(--cb-toggle-track-dark)]' : 'bg-[var(--cb-toggle-track-light)]',
        )}
      />
      <span
        aria-hidden
        className={clsx(
          'pointer-events-none absolute inset-y-1 left-1 flex h-9 w-9 items-center justify-center rounded-full text-lg transition-all duration-300 ease-out shadow-[0_18px_38px_-20px_rgba(15,23,42,0.6)]',
          isDark
            ? 'translate-x-12 bg-[var(--cb-toggle-knob-dark)] text-amber-200'
            : 'translate-x-0 bg-[var(--cb-toggle-knob-light)] text-amber-500',
        )}
      >
        {isDark ? '🌙' : '☀️'}
      </span>
      <span
        aria-hidden
        className="relative z-10 flex w-full items-center justify-between px-4 text-[0.7rem] font-semibold tracking-wide"
      >
        <span
          className={clsx(
            'transition-all duration-200 ease-out',
            isDark ? 'scale-95 opacity-50' : 'scale-100 opacity-100 text-[var(--cb-text)]',
          )}
        >
          Clair
        </span>
        <span
          className={clsx(
            'transition-all duration-200 ease-out',
            isDark ? 'scale-100 opacity-100 text-[var(--cb-text)]' : 'scale-95 opacity-50',
          )}
        >
          Sombre
        </span>
      </span>
    </button>
  )
}
