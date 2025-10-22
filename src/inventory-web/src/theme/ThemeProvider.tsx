import React, { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'

import {
  applyThemeClass,
  getStoredTheme,
  getSystemTheme,
  listenSystemThemeChanges,
  persistTheme,
  type Theme,
} from '@/app/utils/theme'
type ThemeContextValue = { theme: Theme; toggleTheme: () => void; setTheme: (t: Theme) => void }

const ThemeContext = createContext<ThemeContextValue | undefined>(undefined)

const resolveInitialTheme = (): Theme => getStoredTheme() ?? getSystemTheme()

export const ThemeProvider: React.FC<React.PropsWithChildren> = ({ children }) => {
  const [theme, setThemeState] = useState<Theme>(() => resolveInitialTheme())
  const [hasUserPreference, setHasUserPreference] = useState<boolean>(() => getStoredTheme() !== null)

  useEffect(() => {
    persistTheme(theme)
    applyThemeClass(theme)
  }, [theme])

  useEffect(() => {
    if (hasUserPreference) {
      return undefined
    }

    return listenSystemThemeChanges((nextTheme: string) => {
      setThemeState(nextTheme)
    })
  }, [hasUserPreference])

  const setTheme = useCallback((next: Theme) => {
    setHasUserPreference(true)
    setThemeState(next)
  }, [])

  const toggleTheme = useCallback(() => {
    setThemeState((current: string) => {
      const next = current === 'dark' ? 'light' : 'dark'
      setHasUserPreference(true)
      return next
    })
  }, [])

  const value = useMemo<ThemeContextValue>(() => ({
    theme,
    toggleTheme,
    setTheme,
  }), [setTheme, theme, toggleTheme])

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useTheme() {
  const ctx = useContext(ThemeContext)
  if (!ctx) {
    throw new Error('useTheme must be used within ThemeProvider')
  }
  return ctx
}
