import { useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { verifyInventoryInProgress } from '../../api/inventoryApi'
import { Button } from '../../components/Button'
import { Card } from '../../components/Card'
import { EmptyState } from '../../components/EmptyState'
import { LoadingIndicator } from '../../components/LoadingIndicator'
import { useInventory } from '../../contexts/InventoryContext'
import { useAsync } from '../../hooks/useAsync'

export const InventoryConfirmStep = () => {
  const navigate = useNavigate()
  const { selectedUser, countType, location, setSessionId, clearSession } = useInventory()

  useEffect(() => {
    if (!selectedUser) {
      navigate('/inventory/start', { replace: true })
    } else if (!countType) {
      navigate('/inventory/count-type', { replace: true })
    } else if (!location) {
      navigate('/inventory/location', { replace: true })
    }
  }, [countType, location, navigate, selectedUser])

  const { data, loading, error, execute } = useAsync(
    async () => {
      if (!location || !countType) {
        return { hasActive: false }
      }
      return verifyInventoryInProgress(location.id, countType)
    },
    [location?.id, countType],
    { immediate: false },
  )

  useEffect(() => {
    if (location && countType) {
      void execute()
    }
  }, [countType, execute, location])

  const handleStart = () => {
    clearSession()
    setSessionId(null)
    navigate('/inventory/session')
  }

  const handleResume = () => {
    setSessionId(data?.sessionId ?? null)
    navigate('/inventory/session')
  }

  return (
    <div className="flex flex-col gap-6">
      <Card className="flex flex-col gap-4">
        <h2 className="text-2xl font-semibold text-slate-900 dark:text-white">Vérification de la zone</h2>
        <p className="text-sm text-slate-500 dark:text-slate-400">
          Nous vérifions si un comptage est déjà en cours pour ce type et cette zone afin d&apos;éviter les doublons.
        </p>
        {loading && <LoadingIndicator label="Vérification en cours" />}
        {Boolean(error) && (
          <EmptyState
            title="Impossible de vérifier"
            description="La vérification n&apos;a pas pu aboutir. Vous pouvez tout de même démarrer si nécessaire."
          />
        )}
        {!loading && !error && data && !data.hasActive && (
          <EmptyState
            title="Zone disponible"
            description="Aucun comptage en cours. Vous pouvez démarrer immédiatement."
            className="bg-brand-500/10"
          />
        )}
        {!loading && !error && data?.hasActive && (
          <Card className="bg-red-100 text-red-800 dark:bg-red-500/10 dark:text-red-100">
            <h3 className="text-lg font-semibold">Comptage existant</h3>
            <p className="text-sm text-red-700 dark:text-red-200">
              Un inventaire est déjà en cours sur cette zone par {data.owner ?? 'un collègue'}. Choisissez la marche à
              suivre.
            </p>
            <div className="mt-4 flex flex-col gap-3">
              <Button variant="secondary" onClick={handleResume}>
                Reprendre le comptage existant
              </Button>
              <Button variant="ghost" onClick={handleStart}>
                Réinitialiser et démarrer un nouveau comptage
              </Button>
            </div>
          </Card>
        )}
      </Card>
      {!loading && (!data?.hasActive || Boolean(error)) && (
        <Button fullWidth className="py-4" onClick={handleStart}>
          Démarrer le comptage
        </Button>
      )}
    </div>
  )
}
