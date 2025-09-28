import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Button } from '../../components/ui/Button'
import { Input } from '../../components/ui/Input'
import { Card } from '../../components/Card'
import { useInventory } from '../../contexts/InventoryContext'
import { INVENTORY_OPERATORS } from '../../utils/users'

export const InventoryUserStep = () => {
  const navigate = useNavigate()
  const { selectedUser, setSelectedUser } = useInventory()
  const [search, setSearch] = useState('')

  const filteredOperators = useMemo(() => {
    if (!search.trim()) {
      return INVENTORY_OPERATORS
    }
    return INVENTORY_OPERATORS.filter((operator) => operator.toLowerCase().includes(search.toLowerCase()))
  }, [search])

  const handleSelect = (operator: string) => {
    if (operator !== selectedUser) {
      setSelectedUser(operator)
    }
    navigate('/inventory/count-type')
  }

  return (
    <div className="flex flex-col gap-6">
      <Card className="flex flex-col gap-4">
        <h2 className="text-2xl font-semibold text-slate-900 dark:text-white">Qui réalise le comptage ?</h2>
        <p className="text-sm text-slate-500 dark:text-slate-400">
          Sélectionnez votre profil pour assurer la traçabilité des comptages.
        </p>
        <Input
          label="Rechercher"
          name="operatorQuery"
          placeholder="Tapez un prénom"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
        />
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
          {filteredOperators.map((operator) => {
            const isSelected = selectedUser === operator
            return (
              <button
                key={operator}
                type="button"
                onClick={() => handleSelect(operator)}
                className={`rounded-2xl border px-4 py-4 text-center text-sm font-semibold transition-all ${
                  isSelected
                    ? 'border-brand-400 bg-brand-100 text-brand-700 dark:bg-brand-500/20 dark:text-brand-100'
                    : 'border-slate-200 bg-white text-slate-700 hover:border-brand-400/40 hover:bg-brand-50 dark:border-slate-700 dark:bg-slate-900/40 dark:text-slate-200'
                }`}
              >
                {operator}
              </button>
            )
          })}
        </div>
      </Card>
      {selectedUser && (
        <Button fullWidth className="py-4" onClick={() => navigate('/inventory/count-type')}>
          Continuer
        </Button>
      )}
    </div>
  )
}
