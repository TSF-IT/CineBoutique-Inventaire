import type { OperatorDefinition } from '../contexts/OperatorsContext'

export const DEFAULT_OPERATORS: OperatorDefinition[] = [
  { id: 'operator-amelie', name: 'Amélie' },
  { id: 'operator-bruno', name: 'Bruno' },
  { id: 'operator-camille', name: 'Camille' },
  { id: 'operator-david', name: 'David' },
  { id: 'operator-elisa', name: 'Elisa' },
]

export const sortOperatorNames = (operators: OperatorDefinition[]) =>
  [...operators].sort((left, right) => left.name.localeCompare(right.name, undefined, { sensitivity: 'accent' }))
