import { useEffect, useRef } from 'react'
import { Outlet, useLocation, useNavigate } from 'react-router-dom'
import { Stepper } from '../../components/Stepper'
import { Page } from '../../components/Page'
import { useInventory } from '../../contexts/InventoryContext'

const stepIndexByPath: Record<string, number> = {
  '/inventory/start': 0,
  '/inventory/location': 0,
  '/inventory/count-type': 1,
  '/inventory/session': 2,
}

export const InventoryLayout = () => {
  const location = useLocation()
  const navigate = useNavigate()
  const { selectedUser, countType, location: selectedLocation } = useInventory()
  const stepperContainerRef = useRef<HTMLDivElement | null>(null)

  useEffect(() => {
    const path = location.pathname
    if (path === '/inventory/location') {
      if (!selectedUser) {
        navigate('/inventory/start', { replace: true })
      }
      return
    }

    if (path === '/inventory/count-type') {
      if (!selectedUser) {
        navigate('/inventory/start', { replace: true })
        return
      }

      if (!selectedLocation) {
        navigate('/inventory/location', { replace: true })
      }
      return
    }

    if (path === '/inventory/session') {
      if (!selectedUser) {
        navigate('/inventory/start', { replace: true })
        return
      }

      if (!selectedLocation) {
        navigate('/inventory/location', { replace: true })
        return
      }

      if (!countType) {
        navigate('/inventory/count-type', { replace: true })
      }
    }
  }, [countType, location.pathname, navigate, selectedLocation, selectedUser])

  useEffect(() => {
    const container = stepperContainerRef.current
    if (!container) {
      return
    }

    const zoneStepCard = container.querySelector<HTMLElement>('[data-step-index="1"]')
    zoneStepCard?.setAttribute('data-testid', 'step-nav-location')
  }, [location.pathname])

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
              <p>Utilisateur : {selectedUser?.displayName ?? '–'}</p>
              <p>Zone : {selectedLocation?.label ?? '–'}</p>
              <p>Comptage : {countType ?? '–'}</p>
            </div>
          </div>
        </div>
        <div ref={stepperContainerRef}>
          <Stepper activeIndex={activeIndex} />
        </div>
      </div>
      <div className="flex-1">
        <Outlet />
      </div>
    </Page>
  )
}
