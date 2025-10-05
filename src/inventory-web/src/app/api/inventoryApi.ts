import { z } from 'zod'
import { CountType, LocationsSchema } from '../types/inventory'
import type { CompletedRunDetail, CompletedRunSummary, OpenRunSummary } from '../types/inventory'
import type {
  ConflictZoneDetail,
  ConflictZoneSummary,
  InventorySummary,
  Location,
  Product,
} from '../types/inventory'
import http, { HttpError } from '@/lib/api/http'
import { API_BASE } from '@/lib/api/config'
import { areDevFixturesEnabled, cloneDevLocations, findDevLocationById } from './dev/fixtures'

/* ------------------------------------------
   Type helpers (éviter any)
------------------------------------------ */
type WithProblem = { problem?: unknown }
type WithBody = { body?: unknown }
type WithUrl = { url?: string }
type WithProblemBody = { problem?: unknown; body?: unknown }

/* ------------------------------------------
   Utils erreurs / dev
------------------------------------------ */

const isHttpError = (value: unknown): value is HttpError &
  Partial<WithProblem & WithBody & WithUrl> =>
  typeof value === 'object' &&
  value !== null &&
  typeof (value as { status?: unknown }).status === 'number' &&
  typeof (value as { url?: unknown }).url === 'string'

const toHttpError = (message: string, url: string, problem?: unknown, status = 0): HttpError =>
  Object.assign(new Error(message), {
    status,
    url,
    problem,
  })

const shouldUseDevFixtures = (error: unknown, { httpCallSucceeded }: { httpCallSucceeded: boolean }): boolean => {
  if (!import.meta.env.DEV || !areDevFixturesEnabled()) return false
  if (httpCallSucceeded) return false
  if (!error) return true
  if (!isHttpError(error)) return true
  if (error.status === 422) return false
  if (error.status === 0 || error.status >= 500) return true
  return error.status >= 400
}

const logDevFallback = (error: unknown, endpoint: string) => {
  if (!import.meta.env.DEV) return
  const details = isHttpError(error)
    ? `statut HTTP ${error.status}${error.url ? ` (${error.url})` : ''}`
    : error instanceof Error
      ? error.message
      : String(error)
  console.warn(
    `[DEV] Fallback fixtures pour ${endpoint} : ${details}. Activez VITE_DISABLE_DEV_FIXTURES=true pour désactiver cette aide.`,
    error,
  )
}

const tryExtractMessage = (value: unknown): string | null => {
  if (typeof value === 'string') {
    const trimmed = value.trim()
    return trimmed.length > 0 ? trimmed : null
  }
  return null
}

const extractProblemMessage = (problem: unknown): string | null => {
  if (!problem) return null
  if (typeof problem === 'string') return tryExtractMessage(problem)
  if (Array.isArray(problem)) {
    for (const item of problem) {
      const message = extractProblemMessage(item)
      if (message) return message
    }
    return null
  }
  if (typeof problem !== 'object') return null

  const record = problem as Record<string, unknown>
  const candidateKeys = ['message', 'detail', 'title', 'error', 'error_description'] as const
  for (const key of candidateKeys) {
    const message = tryExtractMessage(record[key])
    if (message) return message
  }

  type WithErrors = { errors?: unknown }
  const errorsVal = (record as WithErrors).errors
  if (Array.isArray(errorsVal)) {
    for (const item of errorsVal) {
      const message = extractProblemMessage(item)
      if (message) return message
    }
  }
  return null
}

const getHttpErrorMessage = (error: HttpError): string | null => {
  const fromProblem = extractProblemMessage((error as HttpError & Partial<WithProblem>).problem)
  if (fromProblem) return fromProblem
  const body = (error as HttpError & Partial<WithBody>).body
  return tryExtractMessage(body)
}

const generateUuid = (): string => {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }
  const template = 'xxxxxxxx-xxxx-4xxx-8xxx-xxxxxxxxxxxx'
  return template.replace(/[xy]/g, (char) => {
    const random = Math.floor(Math.random() * 16)
    if (char === 'x') return random.toString(16)
    return ((random & 0x3) | 0x8).toString(16)
  })
}

/* ------------------------------------------
   Normalisation /locations: variantes de clés + coercions de types
------------------------------------------ */

const UPPERCASE_LOCATION_KEY_MAP: Record<string, keyof Location> = {
  Id: 'id',
  Code: 'code',
  Label: 'label',
  IsBusy: 'isBusy',
  BusyBy: 'busyBy',
  ActiveRunId: 'activeRunId',
  ActiveCountType: 'activeCountType',
  ActiveStartedAtUtc: 'activeStartedAtUtc',
}

const UPPERCASE_STATUS_KEY_MAP: Record<string, string> = {
  CountType: 'countType',
  Status: 'status',
  RunId: 'runId',
  OwnerDisplayName: 'ownerDisplayName',
  OwnerUserId: 'ownerUserId',
  StartedAtUtc: 'startedAtUtc',
  CompletedAtUtc: 'completedAtUtc',
}

const SNAKE_LOCATION_KEY_MAP: Record<string, keyof Location> = {
  id: 'id',
  code: 'code',
  label: 'label',
  is_busy: 'isBusy',
  busy_by: 'busyBy',
  active_run_id: 'activeRunId',
  active_count_type: 'activeCountType',
  active_started_at_utc: 'activeStartedAtUtc',
}

const SNAKE_STATUS_KEY_MAP: Record<string, string> = {
  count_type: 'countType',
  status: 'status',
  run_id: 'runId',
  owner_display_name: 'ownerDisplayName',
  owner_user_id: 'ownerUserId',
  started_at_utc: 'startedAtUtc',
  completed_at_utc: 'completedAtUtc',
}

const mapUppercaseStatusObject = (src: unknown): unknown => {
  if (!src || typeof src !== 'object') return src
  const o = src as Record<string, unknown>
  const r: Record<string, unknown> = {}
  for (const [k, v] of Object.entries(o)) r[UPPERCASE_STATUS_KEY_MAP[k] ?? k] = v
  return r
}

const mapSnakeStatusObject = (src: unknown): unknown => {
  if (!src || typeof src !== 'object') return src
  const o = src as Record<string, unknown>
  const r: Record<string, unknown> = {}
  for (const [k, v] of Object.entries(o)) r[SNAKE_STATUS_KEY_MAP[k] ?? k] = v
  return r
}

const mapUppercaseLocationKeys = (arr: unknown): unknown => {
  if (!Array.isArray(arr)) return arr
  return arr.map((item) => {
    if (!item || typeof item !== 'object') return item
    const src = item as Record<string, unknown>

    const mappedEntries = Object.entries(UPPERCASE_LOCATION_KEY_MAP).reduce<Record<string, unknown>>(
      (acc, [sourceKey, targetKey]) => {
        if (sourceKey in src) acc[targetKey] = src[sourceKey]
        return acc
      },
      {},
    )

    const statuses = (src.CountStatuses as unknown) ?? (src.countStatuses as unknown) ?? null
    const countStatuses = Array.isArray(statuses)
      ? statuses.map(mapUppercaseStatusObject)
      : undefined

    return { ...src, ...mappedEntries, ...(countStatuses ? { countStatuses } : {}) }
  })
}

const mapSnakeLocationKeys = (arr: unknown): unknown => {
  if (!Array.isArray(arr)) return arr
  return arr.map((item) => {
    if (!item || typeof item !== 'object') return item
    const src = item as Record<string, unknown>

    const mappedEntries = Object.entries(SNAKE_LOCATION_KEY_MAP).reduce<Record<string, unknown>>(
      (acc, [sourceKey, targetKey]) => {
        if (sourceKey in src) acc[targetKey] = src[sourceKey]
        return acc
      },
      {},
    )

    const statuses =
      (src.count_statuses as unknown) ??
      (src.CountStatuses as unknown) ??
      (src.countStatuses as unknown) ??
      null

    const countStatuses = Array.isArray(statuses)
      ? statuses.map(mapSnakeStatusObject)
      : undefined

    return { ...src, ...mappedEntries, ...(countStatuses ? { countStatuses } : {}) }
  })
}

const createDefaultStatuses = () =>
  [CountType.Count1, CountType.Count2].map((ct) => ({
    countType: ct,
    status: 'not_started',
    runId: null,
    ownerDisplayName: null,
    ownerUserId: null,
    startedAtUtc: null,
    completedAtUtc: null,
  }))

const coerceBool = (v: unknown): boolean | null => {
  if (typeof v === 'boolean') return v
  if (typeof v === 'number') return v !== 0
  if (typeof v === 'string') {
    const t = v.trim().toLowerCase()
    if (!t) return null
    if (t === 'true' || t === '1' || t === 'yes' || t === 'y') return true
    if (t === 'false' || t === '0' || t === 'no' || t === 'n') return false
  }
  return null
}

const coerceGuid = (v: unknown): string | null => {
  if (typeof v === 'string') {
    const t = v.trim()
    const guidRe =
      /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[1-5][0-9a-fA-F]{3}-[89abAB][0-9a-fA-F]{3}-[0-9a-fA-F]{12}$/
    return guidRe.test(t) ? t : null
  }
  return null
}

const coerceIsoOrNull = (v: unknown): string | null => {
  if (v == null) return null
  if (typeof v === 'string') {
    const t = v.trim()
    if (!t) return null
    // si c'est déjà ISO-ish
    if (!Number.isNaN(Date.parse(t))) return new Date(t).toISOString()
    // epoch seconde/millis
    const n = Number(t)
    if (!Number.isNaN(n)) return new Date(n > 1e12 ? n : n * 1000).toISOString()
    return null
  }
  if (v instanceof Date) return v.toISOString()
  if (typeof v === 'number') return new Date(v > 1e12 ? v : v * 1000).toISOString()
  return null
}

const coerceCountType = (v: unknown): number | null => {
  if (typeof v === 'number') return v
  if (typeof v === 'string') {
    const n = Number(v)
    if (!Number.isNaN(n)) return n
  }
  return null
}

const ensureLocationDefaults = (arr: unknown): unknown => {
  if (!Array.isArray(arr)) return arr
  return arr.map((loc) => {
    if (!loc || typeof loc !== 'object') return loc
    const o = { ...(loc as Record<string, unknown>) }

    // alias pour code/label si le back renvoie "name"
    const nameVal = (o as Record<string, unknown>).name
    if (!o.code && typeof nameVal === 'string') o.code = nameVal
    if (!o.label && typeof nameVal === 'string') o.label = nameVal

    // booleans, dates, countType
    const b = coerceBool(o.isBusy)
    if (b !== null) o.isBusy = b
    const ctype = coerceCountType(o.activeCountType)
    if (ctype !== null) o.activeCountType = ctype
    const started = coerceIsoOrNull(o.activeStartedAtUtc)
    o.activeStartedAtUtc = started

    if (typeof o.busyBy === 'string' && o.busyBy.trim().length === 0) o.busyBy = null
    if (o.activeRunId != null && typeof o.activeRunId !== 'string') o.activeRunId = String(o.activeRunId)

    // countStatuses
    if (!Array.isArray(o.countStatuses)) {
      o.countStatuses = createDefaultStatuses()
    } else {
      o.countStatuses = (o.countStatuses as unknown[]).map((s) => {
        const src = mapUppercaseStatusObject(mapSnakeStatusObject(s)) as Record<string, unknown>
        const ct = coerceCountType(src.countType)
        const st = typeof src.status === 'string' ? src.status : 'not_started'
        const runId = src.runId == null ? null : String(src.runId)
        const ownerUserId = coerceGuid(src.ownerUserId) ?? (typeof src.ownerUserId === 'string' ? src.ownerUserId : null)
        return {
          countType: ct ?? CountType.Count1,
          status: st,
          runId,
          ownerDisplayName: typeof src.ownerDisplayName === 'string' ? src.ownerDisplayName : null,
          ownerUserId,
          startedAtUtc: coerceIsoOrNull(src.startedAtUtc),
          completedAtUtc: coerceIsoOrNull(src.completedAtUtc),
        }
      })
    }

    return o
  })
}

const unwrapArrayPayload = (payload: unknown): unknown => {
  if (Array.isArray(payload)) return payload
  if (payload && typeof payload === 'object') {
    const candidateKeys = ['items', 'data', 'results', 'locations'] as const
    for (const key of candidateKeys) {
      const value = (payload as Record<string, unknown>)[key]
      if (Array.isArray(value)) return value
    }
    const arrays = Object.values(payload as Record<string, unknown>).filter(Array.isArray)
    if (arrays.length === 1) return arrays[0]
  }
  return payload
}

const normaliseLocationsPayload = (payload: unknown): unknown => {
  // unwrap wrappers first
  let value = unwrapArrayPayload(payload)

  // null/undefined => []
  if (value == null) return []

  // double-pass mapping: snake_case puis PascalCase
  value = mapSnakeLocationKeys(value)
  value = mapUppercaseLocationKeys(value)

  // coercions et défauts
  value = ensureLocationDefaults(value)

  return value
}

/* ------------------------------------------
   /inventories et autres endpoints
------------------------------------------ */

export const fetchInventorySummary = async (): Promise<InventorySummary> => {
  const data = (await http(`${API_BASE}/inventories/summary`)) as Partial<InventorySummary> | null | undefined

  const sanitizeOpenRun = (run: Partial<OpenRunSummary> | null | undefined): OpenRunSummary => ({
    runId: typeof run?.runId === 'string' ? run.runId : '',
    locationId: typeof run?.locationId === 'string' ? run.locationId : '',
    locationCode: typeof run?.locationCode === 'string' ? run.locationCode : '',
    locationLabel: typeof run?.locationLabel === 'string' ? run.locationLabel : '',
    countType: (run?.countType as CountType) ?? CountType.Count1,
    ownerDisplayName: run?.ownerDisplayName ?? null,
    ownerUserId: typeof run?.ownerUserId === 'string' ? run.ownerUserId : null,
    startedAtUtc: typeof run?.startedAtUtc === 'string' ? run.startedAtUtc : '',
  })

  const sanitizeCompletedRun = (
    run: Partial<CompletedRunSummary> | null | undefined,
  ): CompletedRunSummary => ({
    runId: typeof run?.runId === 'string' ? run.runId : '',
    locationId: typeof run?.locationId === 'string' ? run.locationId : '',
    locationCode: typeof run?.locationCode === 'string' ? run.locationCode : '',
    locationLabel: typeof run?.locationLabel === 'string' ? run.locationLabel : '',
    countType: (run?.countType as CountType) ?? CountType.Count1,
    ownerDisplayName: run?.ownerDisplayName ?? null,
    ownerUserId: typeof run?.ownerUserId === 'string' ? run.ownerUserId : null,
    startedAtUtc: typeof run?.startedAtUtc === 'string' ? run.startedAtUtc : '',
    completedAtUtc: typeof run?.completedAtUtc === 'string' ? run.completedAtUtc : '',
  })

  const openRunDetails = (Array.isArray(data?.openRunDetails) ? data.openRunDetails : []).map(sanitizeOpenRun)
  const completedRunDetails = (Array.isArray(data?.completedRunDetails)
    ? data.completedRunDetails
    : []
  ).map(sanitizeCompletedRun)
  const conflictZones = Array.isArray(data?.conflictZones) ? data!.conflictZones : []

  return {
    activeSessions: data?.activeSessions ?? 0,
    openRuns: data?.openRuns ?? 0,
    completedRuns: data?.completedRuns ?? completedRunDetails.length,
    conflicts: data?.conflicts ?? 0,
    lastActivityUtc: data?.lastActivityUtc ?? null,
    openRunDetails,
    completedRunDetails,
    conflictZones,
  }
}

export const getConflictZonesSummary = async (): Promise<ConflictZoneSummary[]> => {
  const summary = await fetchInventorySummary()
  return summary.conflictZones
}

export const getConflictZoneDetail = async (
  locationId: string,
  signal?: AbortSignal,
): Promise<ConflictZoneDetail> => {
  const data = await http(`${API_BASE}/conflicts/${encodeURIComponent(locationId)}`, {
    signal,
  })
  return data as ConflictZoneDetail
}

export const getCompletedRunDetail = async (runId: string): Promise<CompletedRunDetail> => {
  const data = (await http(`${API_BASE}/inventories/runs/${encodeURIComponent(runId)}`)) as
    | Partial<CompletedRunDetail>
    | null
    | undefined

  const items = Array.isArray(data?.items) ? data!.items : []

  return {
    runId: data?.runId ?? runId,
    locationId: data?.locationId ?? '',
    locationCode: data?.locationCode ?? '',
    locationLabel: data?.locationLabel ?? '',
    countType: (data?.countType as CountType) ?? CountType.Count1,
    ownerDisplayName: data?.ownerDisplayName ?? null,
    ownerUserId: data?.ownerUserId ?? null,
    startedAtUtc: data?.startedAtUtc ?? '',
    completedAtUtc: data?.completedAtUtc ?? '',
    items: items.map((item) => ({
      productId: item?.productId ?? '',
      sku: item?.sku ?? '',
      name: item?.name ?? '',
      ean: typeof item?.ean === 'string' && item.ean.trim().length > 0 ? item.ean : null,
      quantity: typeof item?.quantity === 'number' ? item.quantity : Number(item?.quantity ?? 0),
    })),
  }
}

/* ------------------------------------------
   /locations
------------------------------------------ */

type LocationStatusLoose = Record<string, unknown>;
type LocationLoose = Record<string, unknown>;

const safeJsonFromRaw = (raw: unknown): unknown => {
  if (typeof raw === 'string') {
    const t = raw.trim()
    if (t.length === 0) return null
    try {
      return JSON.parse(t)
    } catch (e) {
      if (import.meta.env.DEV) {
        console.error('Réponse /locations non-JSON:', t.slice(0, 500))
      }
      throw e
    }
  }
  return raw
}

const toStringOrNull = (v: unknown): string | null => {
  if (v == null) return null
  if (typeof v === 'string') {
    const t = v.trim()
    return t.length ? t : null
  }
  return String(v)
}

const toBool = (v: unknown): boolean => {
  if (typeof v === 'boolean') return v
  if (typeof v === 'number') return v !== 0
  if (typeof v === 'string') {
    const t = v.trim().toLowerCase()
    return t === 'true' || t === '1' || t === 'yes' || t === 'y'
  }
  return false
}

const toCountType = (v: unknown): number | null => {
  if (typeof v === 'number') return v
  if (typeof v === 'string') {
    const n = Number(v)
    return Number.isNaN(n) ? null : n
  }
  return null
}

const toDateOrNull = (v: unknown): Date | null => {
  if (v == null) return null
  if (v instanceof Date) return Number.isNaN(v.getTime()) ? null : v
  if (typeof v === 'number') return new Date(v > 1e12 ? v : v * 1000)
  if (typeof v === 'string') {
    const t = v.trim()
    if (!t) return null
    const n = Number(t)
    if (!Number.isNaN(n)) return new Date(n > 1e12 ? n : n * 1000)
    const d = new Date(t)
    return Number.isNaN(d.getTime()) ? null : d
  }
  return null
}

type RunStatus = 'not_started' | 'in_progress' | 'completed'

const toRunStatus = (v: unknown): RunStatus => {
  if (typeof v !== 'string') return 'not_started'
  const t = v.trim().toLowerCase()
  if (t === 'not_started' || t === 'not-started' || t === 'notstarted') return 'not_started'
  if (t === 'in_progress' || t === 'in-progress' || t === 'inprogress' || t === 'running') return 'in_progress'
  if (t === 'completed' || t === 'done' || t === 'finished') return 'completed'
  return 'not_started'
}


/**
 * Dernière passe: forcer exactement les clés/typage que LocationsSchema attend.
 * Sort une liste d’objets propres: { id, code, label, isBusy, busyBy, activeRunId, activeCountType, activeStartedAtUtc, countStatuses: [...] }
 */
const enforceLocationShape = (arr: unknown): Location[] | unknown => {
  if (!Array.isArray(arr)) return arr
  return arr.map((src) => {
    const o = (src ?? {}) as LocationLoose

    // id
    const id = typeof o.id === 'string' ? o.id : toStringOrNull(o['Id'])
    // code / label (fallback depuis "name")
    const name = typeof o.name === 'string' ? o.name : undefined
    const code = typeof o.code === 'string' ? o.code : (typeof o['Code'] === 'string' ? o['Code'] as string : (name ?? ''))
    const label = typeof o.label === 'string' ? o.label : (typeof o['Label'] === 'string' ? o['Label'] as string : (name ?? ''))

    // flags / runtime
    const isBusy = 'isBusy' in o ? toBool(o.isBusy) : toBool(o['IsBusy'])
    const busyByRaw = o.busyBy ?? o['BusyBy']
    const busyBy = toStringOrNull(busyByRaw)

    const activeRunIdRaw = o.activeRunId ?? o['ActiveRunId']
    const activeRunId = toStringOrNull(activeRunIdRaw)

    const activeCountTypeRaw = o.activeCountType ?? o['ActiveCountType']
    // Si pas de valeur exploitable, on laisse UNDEFINED (optionnel) plutôt que NULL (nullable).
    const activeCountType: number | null = (toCountType(activeCountTypeRaw) ?? null);

    const activeStartedAtUtcRaw = o.activeStartedAtUtc ?? o['ActiveStartedAtUtc']
    const activeStartedAtUtc = toDateOrNull(activeStartedAtUtcRaw)

    // statuses
    const statusSrc =
      (o.countStatuses as unknown) ??
      (o['CountStatuses'] as unknown) ??
      (o['count_statuses'] as unknown) ??
      null

    const countStatuses: Array<{
  countType: number
  status: RunStatus
  runId: string | null
  ownerDisplayName: string | null
  ownerUserId: string | null
  startedAtUtc: Date | null
  completedAtUtc: Date | null
}> = Array.isArray(statusSrc)
  ? statusSrc.map((s: LocationStatusLoose) => {
      const s1 = mapSnakeStatusObject(mapUppercaseStatusObject(s)) as LocationStatusLoose
      return {
        countType: toCountType(s1.countType) ?? CountType.Count1,
        status: toRunStatus(s1.status),
        runId: toStringOrNull(s1.runId),
        ownerDisplayName: typeof s1.ownerDisplayName === 'string' ? s1.ownerDisplayName : null,
        ownerUserId: toStringOrNull(s1.ownerUserId),
        startedAtUtc: toDateOrNull(s1.startedAtUtc),
        completedAtUtc: toDateOrNull(s1.completedAtUtc),
      }
    })
  : [
      { countType: CountType.Count1, status: 'not_started', runId: null, ownerDisplayName: null, ownerUserId: null, startedAtUtc: null, completedAtUtc: null },
      { countType: CountType.Count2, status: 'not_started', runId: null, ownerDisplayName: null, ownerUserId: null, startedAtUtc: null, completedAtUtc: null },
    ]


    const shaped: Location = {
      id: (id ?? '') as string,
      code: code ?? '',
      label: label ?? '',
      isBusy,
      busyBy,
      activeRunId,
      // si ton LocationsSchema attend nullable: remplace par `(_activeCountType ?? null) as number | null`
      activeCountType,
      activeStartedAtUtc,
      countStatuses,
    }

    return shaped
  })
}

export const fetchLocations = async (
  shopId: string,
  options?: { countType?: CountType },
): Promise<Location[]> => {
  const normalisedShopId = shopId?.trim()
  if (!normalisedShopId) throw new Error('Aucune boutique sélectionnée.')

  let endpoint = `/locations?shopId=${encodeURIComponent(normalisedShopId)}`
  let httpCallSucceeded = false

  try {
    const searchParams = new URLSearchParams()
    searchParams.set('shopId', normalisedShopId)
    if (options?.countType !== undefined) {
      searchParams.set('countType', String(options.countType))
    }
    const query = searchParams.toString()
    endpoint = `/locations${query ? `?${query}` : ''}`

    const raw = await http(`${API_BASE}${endpoint}`)
    httpCallSucceeded = true

    // null/empty-string => []
    const payload0 = safeJsonFromRaw(raw)
    const payload = payload0 == null ? [] : payload0

    // 1) normalisation large (snake/pascal etc.) déjà faite plus haut dans le fichier
    let normalised = normaliseLocationsPayload(payload)

    // 2) enforcement final: on fabrique EXACTEMENT le shape attendu par LocationsSchema
    normalised = enforceLocationShape(normalised)

    try {
      return LocationsSchema.parse(normalised)
    } catch (err) {




      if (err instanceof z.ZodError) {
  if (import.meta.env.DEV) {
    console.groupCollapsed('Validation /locations failed')
    console.log('issues:', err.issues) // <= ajoute ça
    try {
      const arr = Array.isArray(normalised) ? normalised : []
      console.log('>>> SAMPLE ITEM <<<')
      console.log(JSON.stringify(arr[0], null, 2))
      console.log('>>> FULL ARRAY LEN =', arr.length)
    } catch {}
    console.groupEnd()
  }
  throw toHttpError('Réponse /locations invalide.', `${API_BASE}${endpoint}`, err.flatten(), 422)
}
      throw err
    }
  } catch (error) {
    if (shouldUseDevFixtures(error, { httpCallSucceeded })) {
      logDevFallback(error, `${API_BASE}${endpoint}`)
      return cloneDevLocations()
    }

    if (isHttpError(error)) {
      throw error
    }
    if (import.meta.env.DEV) {
      console.error('Échec de récupération des zones', error)
    }
    const problem = error instanceof z.ZodError ? error.flatten() : error
    throw toHttpError('Impossible de récupérer les zones.', `${API_BASE}${endpoint}`, problem)
  }
}

/* ------------------------------------------
   Produits
------------------------------------------ */

export const fetchProductByEan = async (ean: string): Promise<Product> => {
  const data = await http(`${API_BASE}/products/${encodeURIComponent(ean)}`)
  return data as Product
}

/* ------------------------------------------
   Comptages
------------------------------------------ */

export interface CompleteInventoryRunItem {
  ean: string
  quantity: number
  isManual: boolean
}

export interface CompleteInventoryRunPayload {
  runId?: string | null
  ownerUserId: string
  countType: 1 | 2 | 3
  items: CompleteInventoryRunItem[]
}

export interface StartInventoryRunPayload {
  shopId: string
  ownerUserId: string
  countType: 1 | 2 | 3
}

export interface StartInventoryRunResponse {
  runId: string
  inventorySessionId: string
  locationId: string
  countType: number
  ownerDisplayName: string | null
  ownerUserId: string | null
  startedAtUtc: string
}

export interface CompleteInventoryRunResponse {
  runId: string
  inventorySessionId: string
  locationId: string
  countType: number
  completedAtUtc: string
  itemsCount: number
  totalQuantity: number
}

export const startInventoryRun = async (
  locationId: string,
  payload: StartInventoryRunPayload,
): Promise<StartInventoryRunResponse> => {
  const url = `${API_BASE}/inventories/${encodeURIComponent(locationId)}/start`
  return (await http(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })) as StartInventoryRunResponse
}

export const completeInventoryRun = async (
  locationId: string,
  payload: CompleteInventoryRunPayload,
  signal?: AbortSignal,
): Promise<CompleteInventoryRunResponse> => {
  const url = `${API_BASE}/inventories/${encodeURIComponent(locationId)}/complete`
  try {
    return (await http(url, {
      method: 'POST',
      body: payload,
      signal,
    })) as CompleteInventoryRunResponse
  } catch (error) {
    if (isHttpError(error)) {
      if (error.status === 404 && areDevFixturesEnabled()) {
        const devLocation = findDevLocationById(locationId)
        if (devLocation) {
          logDevFallback(error, url)
          const completionDate = new Date()
          const runId =
            typeof payload.runId === 'string' && payload.runId.trim().length > 0
              ? payload.runId.trim()
              : generateUuid()
          const totalQuantity = payload.items.reduce((sum, item) => sum + item.quantity, 0)
          return {
            runId,
            inventorySessionId: generateUuid(),
            locationId: devLocation.id,
            countType: payload.countType,
            completedAtUtc: completionDate.toISOString(),
            itemsCount: payload.items.length,
            totalQuantity,
          }
        }
      }

      const withPB = error as HttpError & Partial<WithProblemBody>
      const messageFromBody = getHttpErrorMessage(error)
      const problemPayload = withPB.problem ?? (withPB.body ? { body: withPB.body } : undefined)

      if (error.status === 415) {
        throw Object.assign(
          new Error('Unsupported Media Type: requête JSON attendue (Content-Type: application/json).'),
          { status: error.status, problem: problemPayload },
        )
      }

      let message: string
      if (error.status === 400) {
        message = messageFromBody ?? 'Requête invalide.'
      } else if (error.status === 404) {
        message = 'Zone introuvable pour ce comptage.'
      } else {
        message = messageFromBody ?? `Impossible de terminer le comptage (HTTP ${error.status}).`
      }

      throw Object.assign(new Error(message), {
        status: error.status,
        problem: problemPayload,
      })
    }

    throw error
  }
}

export const restartInventoryRun = async (locationId: string, countType: CountType): Promise<void> => {
  const searchParams = new URLSearchParams({ countType: String(countType) })
  await http(`${API_BASE}/inventories/${locationId}/restart?${searchParams.toString()}`, {
    method: 'POST',
  })
}

export const releaseInventoryRun = async (
  locationId: string,
  runId: string,
  ownerUserId: string,
): Promise<void> => {
  const url = `${API_BASE}/inventories/${encodeURIComponent(locationId)}/release`
  try {
    await http(url, {
      method: 'POST',
      body: { runId, ownerUserId },
    })
  } catch (error) {
    if (isHttpError(error)) {
      const messageFromBody = getHttpErrorMessage(error)
      const errHttp = error as HttpError & Partial<WithUrl & WithProblem>

      let message = `Impossible de libérer le comptage (HTTP ${errHttp.status}).`
      if (errHttp.status === 404) message = messageFromBody ?? 'Comptage introuvable.'
      else if (errHttp.status === 409)
        message = messageFromBody ?? 'Comptage déjà détenu par un autre utilisateur.'
      else if (errHttp.status === 400) message = messageFromBody ?? 'Requête invalide.'
      else if (messageFromBody) message = messageFromBody

      throw Object.assign(new Error(message), {
        status: errHttp.status,
        url: errHttp.url,
        problem: errHttp.problem,
      })
    }

    throw error
  }
}
