const sanitize = (value: string | null | undefined): string => value?.trim() ?? ''

const normalize = (value: string | null | undefined): string => sanitize(value).toLowerCase()

export const isLocationLabelRedundant = (code: string | null | undefined, label: string | null | undefined): boolean => {
  const safeLabel = sanitize(label)
  if (!safeLabel) {
    return true
  }

  const safeCode = sanitize(code)
  if (!safeCode) {
    return false
  }

  const normalizedCode = normalize(safeCode)
  const normalizedLabel = normalize(safeLabel)

  if (!normalizedLabel) {
    return true
  }

  if (normalizedLabel === normalizedCode) {
    return true
  }

  const labelWithoutZonePrefix = normalizedLabel.replace(/^zone\s+/u, '').trim()
  if (labelWithoutZonePrefix === normalizedCode) {
    return true
  }

  return false
}

export const getLocationDisplayName = (
  code: string | null | undefined,
  label: string | null | undefined,
): string => {
  const safeCode = sanitize(code)
  const safeLabel = sanitize(label)
  if (!safeLabel) {
    return safeCode
  }
  if (!safeCode) {
    return safeLabel
  }
  return isLocationLabelRedundant(code, label) ? safeCode : safeLabel
}
