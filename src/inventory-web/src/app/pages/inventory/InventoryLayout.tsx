import { Outlet, useLocation, useNavigate } from 'react-router-dom'
import { Stepper } from '../../components/Stepper'
import { Page } from '../../components/Page'
import { useInventory } from '../../contexts/InventoryContext'

const STEPS = ['Utilisateur', 'Type de comptage', 'Zone', 'Scan']

const stepIndexByPath: Record<string, number> = {
  '/inventory/start': 0,
  '/inventory/count-type': 1,
  '/inventory/location': 2,
  '/inventory/session': 3,
}

export const InventoryLayout = () => {
  const location = useLocation()
  const navigate = useNavigate()
  const { selectedUser, countType, location: selectedLocation } = useInventory()

  if (location.pathname === '/inventory') {
    navigate('/inventory/start', { replace: true })
  }

  const activeIndex = stepIndexByPath[location.pathname] ?? 0

  return (
    <Page className="gap-8">
      <div className="flex flex-col gap-6">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <p className="text-sm uppercase tracking-[0.25em] text-brand-500 dark:text-brand-200">CinéBoutique</p>
            <h1 className="text-3xl font-bold text-slate-900 dark:text-white">Assistant d&apos;inventaire</h1>
          </div>
          <div className="flex flex-col items-end gap-3 sm:flex-row sm:items-center">
            <div className="hidden text-right text-xs text-slate-500 dark:text-slate-400 sm:block">
              <p>Utilisateur : {selectedUser ?? '–'}</p>
              <p>Comptage : {countType ?? '–'}</p>
              <p>Zone : {selectedLocation?.label ?? '–'}</p>
            </div>
          </div>
        </div>
        <Stepper steps={STEPS} activeIndex={activeIndex} />
      </div>
      <div className="flex-1">
        <Outlet />
      </div>
    </Page>
  )
}
