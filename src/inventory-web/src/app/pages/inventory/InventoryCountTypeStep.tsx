import { useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { Button } from '../../components/ui/Button'
import { Card } from '../../components/Card'
import { useInventory } from '../../contexts/InventoryContext'

export const InventoryCountTypeStep = () => {
  const navigate = useNavigate()
  const { selectedUser, countType, setCountType, location } = useInventory()

  useEffect(() => {
    if (!selectedUser) {
      navigate('/inventory/start', { replace: true })
    } else if (!location) {
      navigate('/inventory/location', { replace: true })
    }
  }, [location, navigate, selectedUser])

  const handleSelect = (type: 1 | 2) => {
    setCountType(type)
  }

  return (
    <div className="flex flex-col gap-6">
      <Card className="flex flex-col gap-4">
        <h2 className="text-2xl font-semibold text-slate-900 dark:text-white">Quel type de comptage ?</h2>
        <p className="text-sm text-slate-500 dark:text-slate-400">
          Sélectionnez le niveau de précision souhaité. Le comptage double implique une double validation.
        </p>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          {[1, 2].map((option) => {
            const isSelected = countType === option
            return (
              <button
                key={option}
                type="button"
                onClick={() => handleSelect(option as 1 | 2)}
                className={`flex flex-col gap-2 rounded-3xl border px-6 py-6 text-left transition-all ${
                  isSelected
                    ? 'border-brand-400 bg-brand-100 text-brand-700 dark:bg-brand-500/20 dark:text-brand-100'
                    : 'border-slate-200 bg-white text-slate-800 hover:border-brand-400/40 hover:bg-brand-50 dark:border-slate-700 dark:bg-slate-900/40 dark:text-slate-200'
                }`}
              >
                <span className="text-4xl font-bold">Comptage n°{option}</span>
                <span className="text-sm text-slate-500 dark:text-slate-400">
                  {option === 1
                    ? 'Rapide et efficace pour les zones à faible risque.'
                    : 'Deux passages consécutifs pour fiabiliser les zones sensibles.'}
                </span>
              </button>
            )
          })}
        </div>
      </Card>
      {countType && (
        <Button fullWidth className="py-4" onClick={() => navigate('/inventory/session')}>
          Passer au scan
        </Button>
      )}
    </div>
  )
}
