import type { ParsedRfid, RfidVerdict } from '@/lib/rfid'

const STORAGE_KEY = 'inventory-scan-log'
const MAX_ENTRIES = 50

export interface SessionLogEntry {
  timestamp: string
  input: string
  normalized: string
  suffix?: string
  verdict: RfidVerdict
}

const getStorage = (): Storage | null => {
  if (typeof window === 'undefined') {
    return null
  }
  try {
    return window.sessionStorage
  } catch {
    return null
  }
}

const readEntries = (): SessionLogEntry[] => {
  const storage = getStorage()
  if (!storage) {
    return []
  }
  try {
    const raw = storage.getItem(STORAGE_KEY)
    if (!raw) {
      return []
    }
    const parsed = JSON.parse(raw) as SessionLogEntry[]
    if (!Array.isArray(parsed)) {
      return []
    }
    return parsed
  } catch {
    return []
  }
}

const writeEntries = (entries: SessionLogEntry[]) => {
  const storage = getStorage()
  if (!storage) {
    return
  }
  try {
    storage.setItem(STORAGE_KEY, JSON.stringify(entries))
  } catch {
    // Ignore storage errors (quota, private mode, etc.)
  }
}

export const logRfidEvent = ({
  input,
  normalized,
  suffix,
  verdict,
}: {
  input: string
  normalized: string
  suffix?: string
  verdict: RfidVerdict
}) => {
  const entry: SessionLogEntry = {
    timestamp: new Date().toISOString(),
    input,
    normalized,
    suffix,
    verdict,
  }
  const entries = [entry, ...readEntries()].slice(0, MAX_ENTRIES)
  writeEntries(entries)
}

export const getSessionLog = (): SessionLogEntry[] => readEntries()

export const clearSessionLog = () => {
  const storage = getStorage()
  storage?.removeItem(STORAGE_KEY)
}

export const logParsedRfid = (parsed: ParsedRfid) => {
  logRfidEvent({
    input: parsed.original,
    normalized: parsed.normalized,
    suffix: parsed.suffix,
    verdict: parsed.verdict,
  })
}
