import type { Shop } from '@/types/shop'

export type EntityId = 'cineboutique' | 'lumiere'

export type EntityDefinition = {
  id: EntityId
  label: string
  description: string
  match: (shop: Shop) => boolean
}

const normalize = (value: string) =>
  value
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .toLowerCase()

export const ENTITY_DEFINITIONS: EntityDefinition[] = [
  {
    id: 'cineboutique',
    label: 'CinéBoutique',
    description: 'Inventaires et équipes CinéBoutique.',
    match: (shop) => normalize(shop.name).includes('cineboutique'),
  },
  {
    id: 'lumiere',
    label: 'Lumière',
    description: 'Réseau Lumière et partenaires.',
    match: (shop) => normalize(shop.name).includes('lumiere'),
  },
]
