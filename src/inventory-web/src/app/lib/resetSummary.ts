import type { ResetShopInventoryResponse } from '@/app/api/inventoryApi'

const formatCount = (value: number, singular: string, plural: string) =>
  `${value} ${value > 1 ? plural : singular}`

export const formatResetInventorySummary = (
  summary: ResetShopInventoryResponse | null | undefined,
): string | null => {
  if (!summary) {
    return null
  }

  const parts = [
    formatCount(summary.zonesCleared, 'zone réinitialisée', 'zones réinitialisées'),
    formatCount(summary.runsCleared, 'comptage supprimé', 'comptages supprimés'),
    formatCount(summary.linesCleared, 'ligne supprimée', 'lignes supprimées'),
    formatCount(summary.conflictsCleared, 'conflit supprimé', 'conflits supprimés'),
  ]

  return parts.join(' · ')
}
