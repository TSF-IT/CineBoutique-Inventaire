const fromEnv = import.meta.env.VITE_API_BASE?.trim()
const isAbsolute = (value: string) => /^https?:\/\//i.test(value)

export const API_BASE = fromEnv && isAbsolute(fromEnv) ? fromEnv : fromEnv || '/api'

if (import.meta.env.DEV) {
  console.info('[DEV] API_BASE =', API_BASE)
}
