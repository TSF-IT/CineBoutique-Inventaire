const fromEnv = import.meta.env.VITE_API_BASE?.trim()
const isAbsolute = (url: string) => /^https?:\/\//i.test(url)

export const API_BASE = fromEnv && isAbsolute(fromEnv) ? fromEnv : fromEnv || '/api'
