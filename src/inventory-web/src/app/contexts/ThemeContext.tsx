import type { ReactNode } from 'react'
import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import { applyThemeClass, getStoredTheme, listenSystemThemeChanges, persistTheme, resolveInitialTheme } from '../utils/theme'
import type { Theme } from '../utils/theme'

interface ThemeContextValue {
  theme: Theme
  setTheme: (theme: Theme) => void
  toggleTheme: () => void
}

const ThemeContext = createContext<ThemeContextValue | undefined>(undefined)

export const ThemeProvider = ({ children }: { children: ReactNode }) => {
  const [theme, setThemeState] = useState<Theme>(() => resolveInitialTheme())
  const [userPreference, setUserPreference] = useState<Theme | null>(() => getStoredTheme())

  useEffect(() => {
    applyThemeClass(theme)
  }, [theme])

  useEffect(() => {
    if (userPreference) {
      return
    }
    const unsubscribe = listenSystemThemeChanges((nextTheme) => {
      setThemeState(nextTheme)
    })
    return unsubscribe
  }, [userPreference])

  const setTheme = useCallback((nextTheme: Theme) => {
    setThemeState(nextTheme)
    setUserPreference(nextTheme)
    persistTheme(nextTheme)
  }, [])

  const toggleTheme = useCallback(() => {
    setTheme(theme === 'dark' ? 'light' : 'dark')
  }, [setTheme, theme])

  const value = useMemo(
    () => ({
      theme,
      setTheme,
      toggleTheme,
    }),
    [setTheme, theme, toggleTheme],
  )

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export const useTheme = () => {
  const context = useContext(ThemeContext)
  if (!context) {
    throw new Error('useTheme doit être utilisé dans un ThemeProvider')
  }
  return context
}
