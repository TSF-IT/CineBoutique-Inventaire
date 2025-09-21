export type Theme = 'light' | 'dark'

const THEME_STORAGE_KEY = 'theme'

const isBrowser = () => typeof window !== 'undefined'

export const getSystemTheme = (): Theme => {
  if (!isBrowser()) {
    return 'light'
  }
  if (typeof window.matchMedia !== 'function') {
    return 'light'
  }
  try {
    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
  } catch (error) {
    if (import.meta.env.DEV) {
      console.warn('Impossible de déterminer le thème système', error)
    }
    return 'light'
  }
}

export const getStoredTheme = (): Theme | null => {
  if (!isBrowser()) {
    return null
  }
  try {
    const stored = window.localStorage.getItem(THEME_STORAGE_KEY)
    return stored === 'dark' || stored === 'light' ? stored : null
  } catch (error) {
    if (import.meta.env.DEV) {
      console.warn('Lecture du thème utilisateur impossible', error)
    }
    return null
  }
}

export const persistTheme = (theme: Theme) => {
  if (!isBrowser()) {
    return
  }
  try {
    window.localStorage.setItem(THEME_STORAGE_KEY, theme)
  } catch (error) {
    if (import.meta.env.DEV) {
      console.warn('Impossible de persister le thème utilisateur', error)
    }
  }
}

export const applyThemeClass = (theme: Theme) => {
  if (!isBrowser()) {
    return
  }
  const root = document.documentElement
  root.classList.toggle('dark', theme === 'dark')
  root.classList.toggle('light', theme === 'light')
  root.dataset.theme = theme
}

export const resolveInitialTheme = (): Theme => getStoredTheme() ?? getSystemTheme()

export const initializeTheme = () => {
  const theme = resolveInitialTheme()
  applyThemeClass(theme)
}

export const listenSystemThemeChanges = (callback: (theme: Theme) => void) => {
  if (!isBrowser() || typeof window.matchMedia !== 'function') {
    return () => {}
  }
  const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)')
  const handler = (event: MediaQueryListEvent) => {
    callback(event.matches ? 'dark' : 'light')
  }
  mediaQuery.addEventListener('change', handler)
  return () => mediaQuery.removeEventListener('change', handler)
}
