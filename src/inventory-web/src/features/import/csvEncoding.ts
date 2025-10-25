export type CsvEncoding = "auto" | "utf-8" | "windows-1252" | "latin1" | "iso-8859-1" | "iso-8859-15" | "macintosh" | "cp437" | "hybrid"
export type CsvEncodingOption = Exclude<CsvEncoding, "auto">

export const CSV_ENCODING_OPTIONS: Array<{ value: CsvEncoding; label: string }> = [
  { value: "auto", label: "Détection automatique" },
  { value: "utf-8", label: "UTF-8 (Unicode)" },
  { value: "windows-1252", label: "Windows-1252 (Europe occidentale)" },
  { value: "iso-8859-1", label: "ISO-8859-1 (Latin-1)" },
  { value: "iso-8859-15", label: "ISO-8859-15 (Euro)" },
  { value: "macintosh", label: "Mac Roman" },
  { value: "cp437", label: "IBM PC (CP437)" },
  { value: "hybrid", label: "Mode PC (0x80-0x9F)" },
]

const ACCENT_CHARS = new Set("àâäæçéèêëîïôœùûüÿÀÂÄÆÇÉÈÊËÎÏÔŒÙÛÜŸ")
const SUSPICIOUS_CP1252_CHARS = new Set("‚ƒ„…†‡ˆ‰Š‹Ž‘’“”•–—˜™š›žŒœŸ")
const ALLOWED_CONTROL_CODES = new Set([9, 10, 13])

const CP437_TABLE = [
  "Ç", "ü", "é", "â", "ä", "à", "å", "ç", "ê", "ë", "è", "ï", "î", "ì", "Ä", "Å",
  "É", "æ", "Æ", "ô", "ö", "ò", "û", "ù", "ÿ", "Ö", "Ü", "¢", "£", "¥", "₧", "ƒ",
  "á", "í", "ó", "ú", "ñ", "Ñ", "ª", "º", "¿", "⌐", "¬", "½", "¼", "¡", "«", "»",
  "░", "▒", "▓", "│", "┤", "╡", "╢", "╖", "╕", "╣", "║", "╗", "╝", "╜", "╛", "┐",
  "└", "┴", "┬", "├", "─", "┼", "╞", "╟", "╚", "╔", "╩", "╦", "╠", "═", "╬", "╧",
  "╨", "╤", "╥", "╙", "╘", "╒", "╓", "╫", "╪", "┘", "┌", "█", "▄", "▌", "▐", "▀",
  "α", "ß", "Γ", "π", "Σ", "σ", "µ", "τ", "Φ", "Θ", "Ω", "δ", "∞", "φ", "ε", "∩",
  "≡", "±", "≥", "≤", "⌠", "⌡", "÷", "≈", "°", "∙", "·", "√", "ⁿ", "²", "■", "\u00a0",
]

const scoreDecodedText = (value: string) => {
  let replacement = 0
  let control = 0
  for (const char of value) {
    const code = char.codePointAt(0) ?? 0
    if (code === 0xfffd) {
      replacement += 1
      continue
    }
    if ((code < 32 && !ALLOWED_CONTROL_CODES.has(code)) || (code >= 0x7f && code <= 0x9f)) {
      control += 1
    }
  }
  return replacement * 200 + control
}

const countAccents = (value: string) => {
  let count = 0
  for (const char of value) {
    if (ACCENT_CHARS.has(char)) {
      count += 1
    }
  }
  return count
}

const countSuspicious = (value: string) => {
  let count = 0
  for (const char of value) {
    if (SUSPICIOUS_CP1252_CHARS.has(char)) {
      count += 1
    }
  }
  return count
}

const decodeWithCp437 = (bytes: Uint8Array) => {
  let result = ""
  for (let index = 0; index < bytes.length; index += 1) {
    const code = bytes[index]
    if (code < 0x80) {
      result += String.fromCharCode(code)
      continue
    }
    result += CP437_TABLE[code - 0x80] ?? String.fromCharCode(code)
  }
  return result
}

const decodeHybridLatin = (bytes: Uint8Array) => {
  let result = ""
  for (let index = 0; index < bytes.length; index += 1) {
    const code = bytes[index]
    if (code < 0x80) {
      result += String.fromCharCode(code)
    } else if (code <= 0x9f) {
      result += CP437_TABLE[code - 0x80] ?? String.fromCharCode(code)
    } else {
      result += String.fromCharCode(code)
    }
  }
  return result
}

const decodeWithTextDecoder = (bytes: Uint8Array, label: string, fatal = false): string | null => {
  try {
    const decoder = fatal ? new TextDecoder(label, { fatal: true }) : new TextDecoder(label)
    return decoder.decode(bytes)
  } catch {
    return null
  }
}

export type CsvDecodingResult = {
  text: string
  detectedEncoding: CsvEncodingOption
}

const FALLBACK_DECODER_ORDER: CsvEncodingOption[] = [
  "utf-8",
  "windows-1252",
  "latin1",
  "iso-8859-1",
  "iso-8859-15",
  "macintosh",
  "hybrid",
  "cp437",
]

export const decodeCsvBuffer = (
  buffer: ArrayBuffer,
  encoding: CsvEncoding = "auto",
): CsvDecodingResult => {
  const bytes = new Uint8Array(buffer)

  const decodeWithStrategy = (strategy: CsvEncodingOption): string | null => {
    switch (strategy) {
      case "utf-8":
        return decodeWithTextDecoder(bytes, "utf-8", true)
      case "windows-1252":
        return decodeWithTextDecoder(bytes, "windows-1252")
      case "latin1":
        return decodeWithTextDecoder(bytes, "latin1")
      case "iso-8859-1":
        return decodeWithTextDecoder(bytes, "iso-8859-1")
      case "iso-8859-15":
        return decodeWithTextDecoder(bytes, "iso-8859-15")
      case "macintosh":
        return decodeWithTextDecoder(bytes, "macintosh")
      case "cp437":
        return decodeWithCp437(bytes)
      case "hybrid":
        return decodeHybridLatin(bytes)
      default:
        return null
    }
  }

  if (encoding !== "auto") {
    const forced = decodeWithStrategy(encoding as CsvEncodingOption)
    if (forced !== null) {
      return { text: forced, detectedEncoding: encoding as CsvEncodingOption }
    }
    // forced decoding failed, fall back to automatic detection
  }

  let bestText: string | null = null
  let bestEncoding: CsvEncodingOption = "utf-8"
  let bestScore = Number.POSITIVE_INFINITY
  let bestAccentBoost = Number.NEGATIVE_INFINITY
  let lowestSuspiciousCount = Number.POSITIVE_INFINITY

  for (const candidate of FALLBACK_DECODER_ORDER) {
    const decoded = decodeWithStrategy(candidate)
    if (typeof decoded !== "string") {
      continue
    }

    const score = scoreDecodedText(decoded)
    const accentCount = countAccents(decoded)
    const suspiciousCount = countSuspicious(decoded)

    const shouldUpdate =
      score < bestScore ||
      (score === bestScore && suspiciousCount < lowestSuspiciousCount) ||
      (score === bestScore &&
        suspiciousCount === lowestSuspiciousCount &&
        accentCount > bestAccentBoost)

    if (shouldUpdate) {
      bestScore = score
      bestAccentBoost = accentCount
      lowestSuspiciousCount = suspiciousCount
      bestText = decoded
      bestEncoding = candidate
    }
  }

  if (bestText !== null) {
    return { text: bestText, detectedEncoding: bestEncoding }
  }

  try {
    const fallback = new TextDecoder().decode(bytes)
    return { text: fallback, detectedEncoding: "utf-8" }
  } catch {
    let direct = ""
    for (let index = 0; index < bytes.length; index += 1) {
      direct += String.fromCharCode(bytes[index])
    }
    return { text: direct, detectedEncoding: "utf-8" }
  }
}
