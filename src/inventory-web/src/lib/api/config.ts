const fromEnv = import.meta.env.VITE_API_BASE?.trim()
const isRelative = (value: string) => value.startsWith('/')
const absolute = fromEnv && /^https?:\/\//i.test(fromEnv)

export const API_BASE = fromEnv && isRelative(fromEnv) ? fromEnv : '/api'

if (import.meta.env.DEV && absolute) {
  console.warn('[DEV] Les URLs absolues sont ignorées. Utilisez un proxy HTTPS et des chemins relatifs (/api).')
}

if (import.meta.env.DEV && !absolute) {
  console.debug('[DEV] API_BASE =', API_BASE)
}
