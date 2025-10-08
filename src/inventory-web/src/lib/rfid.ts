export type RfidClassification = 'digits' | 'alnum' | 'letters' | 'mixed'

export interface NormalizedRfidResult {
  original: string
  normalized: string
  suffix?: string
  notes: string[]
}

export interface RfidVerdict {
  ok: boolean
  level: 'green' | 'amber' | 'red'
  type: RfidClassification
  length: number
  reason?: string
}

const TRAILING_SUFFIX_REGEX = /#(\d+)$/
const NOISE_CHARACTERS_REGEX = /[ _'°.]/g
const DIGIT_LIKE_TRAILING_C_REGEX = /^(\d+)C$/

export const normalizeRfid = (input: string): NormalizedRfidResult => {
  const notes: string[] = []
  const original = input

  let normalized = input

  const trimmed = normalized.trim()
  if (trimmed !== normalized) {
    notes.push('Espaces retirés en début/fin')
    normalized = trimmed
  }

  const uppercased = normalized.toUpperCase()
  if (uppercased !== normalized) {
    notes.push('Passage en majuscules')
    normalized = uppercased
  }

  const replacedCedilla = normalized.replace(/ç/gi, (match) => {
    notes.push(`Caractère ${match} converti en C`)
    return 'C'
  })
  normalized = replacedCedilla

  const cleaned = normalized.replace(NOISE_CHARACTERS_REGEX, '')
  if (cleaned !== normalized) {
    notes.push('Caractères parasites retirés')
    normalized = cleaned
  }

  let suffix: string | undefined
  const suffixMatch = normalized.match(TRAILING_SUFFIX_REGEX)
  if (suffixMatch) {
    suffix = suffixMatch[1]
    normalized = normalized.slice(0, -suffixMatch[0].length)
    notes.push(`Suffixe #${suffix} extrait`)
  }

  return {
    original,
    normalized,
    suffix,
    notes,
  }
}

export const classifyRfid = (value: string): RfidClassification => {
  if (!value) {
    return 'mixed'
  }

  if (/^\d+$/.test(value) || DIGIT_LIKE_TRAILING_C_REGEX.test(value)) {
    return 'digits'
  }

  if (/^[A-Z]+$/.test(value)) {
    return 'letters'
  }

  if (/^[A-Z0-9]+$/.test(value)) {
    return 'alnum'
  }

  return 'mixed'
}

const isLengthIn = (value: number, allowed: number[]) => allowed.includes(value)

const describeLengthIssue = (type: RfidClassification, length: number) => {
  switch (type) {
    case 'digits':
      return `Longueur ${length} non supportée pour un code numérique`
    case 'alnum':
      return `Longueur ${length} non supportée pour un code alphanumérique`
    case 'letters':
      return `Longueur ${length} non supportée pour un code alphabétique`
    default:
      return 'Format mixte non supporté'
  }
}

export const validateRfid = (value: string): RfidVerdict => {
  const type = classifyRfid(value)

  const digitsLength = value.replace(/\D/g, '').length
  const length = type === 'digits' ? digitsLength : value.length

  if (type === 'mixed') {
    return {
      ok: false,
      level: 'red',
      type,
      length,
      reason: 'Format mixte non supporté',
    }
  }

  if (type === 'digits') {
    if (isLengthIn(length, [5, 12, 13])) {
      return { ok: true, level: 'green', type, length }
    }
    if (isLengthIn(length, [7, 8, 10, 11, 20])) {
      return { ok: true, level: 'amber', type, length }
    }
    return { ok: false, level: 'red', type, length, reason: describeLengthIssue(type, length) }
  }

  if (type === 'alnum') {
    if (length >= 6 && length <= 12) {
      return { ok: true, level: 'green', type, length }
    }
    if ((length >= 4 && length <= 5) || (length >= 13 && length <= 16)) {
      return { ok: true, level: 'amber', type, length }
    }
    return { ok: false, level: 'red', type, length, reason: describeLengthIssue(type, length) }
  }

  // letters
  if (length >= 2 && length <= 10) {
    return { ok: true, level: 'green', type, length }
  }

  return { ok: false, level: 'red', type, length, reason: describeLengthIssue(type, length) }
}

export interface ParsedRfid {
  original: string
  normalized: string
  suffix?: string
  verdict: RfidVerdict
  notes: string[]
}

export const parseRfid = (input: string): ParsedRfid => {
  const normalizedResult = normalizeRfid(input)
  const verdict = validateRfid(normalizedResult.normalized)
  return {
    original: normalizedResult.original,
    normalized: normalizedResult.normalized,
    suffix: normalizedResult.suffix,
    verdict,
    notes: normalizedResult.notes,
  }
}
