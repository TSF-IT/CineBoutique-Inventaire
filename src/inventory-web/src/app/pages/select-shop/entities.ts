import type { Shop } from '@/types/shop'

export type EntityId = 'cineboutique' | 'lumiere'

export type EntityDefinition = {
  id: EntityId
  label: string
  description: string
}

export type EntityCardModel = {
  definition: EntityDefinition
  matches: Shop[]
  primaryShop: Shop | null
}

const removeDiacritics = (value: string) => value.normalize('NFD').replace(/[\u0300-\u036f]/g, '')

const normalizeName = (value: string) =>
  removeDiacritics(value)
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, ' ')
    .trim()

const tokenize = (value: string) => normalizeName(value).split(/\s+/).filter(Boolean)
const toWordSet = (value: string) => new Set(tokenize(value))

const containsAnySubstring = (value: string, candidates: readonly string[]) =>
  candidates.some((candidate) => value.includes(candidate))

const containsAnyWord = (words: ReadonlySet<string>, candidates: readonly string[]) =>
  candidates.some((candidate) => words.has(candidate))

const CINE_BOUTIQUE_SUBSTRINGS = ['cineboutique', 'cine boutique'] as const
const CINE_BOUTIQUE_WORDS = ['cineboutique', 'cb'] as const

const LUMIERE_SUBSTRINGS = ['lumiere', 'lumieres'] as const
const LUMIERE_WORDS = ['lumiere', 'lumieres', 'lmr'] as const

const isCineBoutique = (normalizedName: string, words: ReadonlySet<string>) =>
  containsAnySubstring(normalizedName, CINE_BOUTIQUE_SUBSTRINGS) ||
  containsAnyWord(words, CINE_BOUTIQUE_WORDS)

const isLumiere = (normalizedName: string, words: ReadonlySet<string>) =>
  containsAnySubstring(normalizedName, LUMIERE_SUBSTRINGS) || containsAnyWord(words, LUMIERE_WORDS)

const selectPreferredShop = (matches: Shop[], entityId: EntityId): Shop | null => {
  if (matches.length === 0) {
    return null
  }

  const preferred = matches.find((shop) => {
    const normalized = normalizeName(shop.name)
    const words = toWordSet(shop.name)
    if (entityId === 'cineboutique') {
      return isCineBoutique(normalized, words)
    }
    if (entityId === 'lumiere') {
      return containsAnySubstring(normalized, LUMIERE_SUBSTRINGS)
    }
    return false
  })

  return preferred ?? matches[0]
}

export const resolveEntityIdForShop = (shop: Shop): EntityId => {
  const normalized = normalizeName(shop.name)
  const words = toWordSet(shop.name)

  if (isCineBoutique(normalized, words)) {
    return 'cineboutique'
  }

  if (isLumiere(normalized, words)) {
    return 'lumiere'
  }

  // Fallback déterministe : les shops marqués "boutique" sont rattachés à CinéBoutique, le reste à Lumière.
  if (shop.kind === 'boutique') {
    return 'cineboutique'
  }

  return 'lumiere'
}

export const ENTITY_DEFINITIONS: EntityDefinition[] = [
  {
    id: 'cineboutique',
    label: 'CinéBoutique',
    description: 'Inventaires et équipes CinéBoutique.',
  },
  {
    id: 'lumiere',
    label: 'Lumière',
    description: 'Réseau Lumière et partenaires.',
  },
]

export const buildEntityCards = (shops: Shop[]): EntityCardModel[] => {
  const buckets = new Map<EntityId, Shop[]>([
    ['cineboutique', []],
    ['lumiere', []],
  ])

  for (const shop of shops) {
    const entityId = resolveEntityIdForShop(shop)
    const bucket = buckets.get(entityId)
    if (!bucket) {
      continue
    }
    bucket.push(shop)
  }

  return ENTITY_DEFINITIONS.map<EntityCardModel>((definition) => {
    const matches = buckets.get(definition.id) ?? []
    return {
      definition,
      matches,
      primaryShop: selectPreferredShop(matches, definition.id),
    }
  })
}
