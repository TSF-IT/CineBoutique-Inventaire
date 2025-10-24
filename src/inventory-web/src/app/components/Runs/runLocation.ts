export interface RunWithLocation {
  locationCode: string | null | undefined
  locationLabel: string | null | undefined
}

export const toValidLocationCode = (value: string | null | undefined) => {
  const trimmed = value?.trim()
  return trimmed && trimmed.length > 0 ? trimmed : null
}

export const toValidLocationLabel = (value: string | null | undefined) => {
  const trimmed = value?.trim()
  return trimmed && trimmed.length > 0 ? trimmed : null
}

export const resolveZoneLabel = <TRun extends RunWithLocation>(run: TRun) =>
  toValidLocationLabel(run.locationLabel) ?? toValidLocationCode(run.locationCode) ?? 'Zone inconnue'

export const formatZoneTitle = <TRun extends RunWithLocation>(run: TRun) => resolveZoneLabel(run)
