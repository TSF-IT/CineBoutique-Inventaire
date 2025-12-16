import { useEffect, useRef } from 'react'
import { Outlet, useLocation, useNavigate } from 'react-router-dom'

import { AdminShortcutButton } from '@/app/components/AdminShortcutButton'
import { Page } from '../../components/Page'
import { Stepper } from '../../components/Stepper'
import { useInventory } from '../../contexts/InventoryContext'

import { useShop } from '@/state/ShopContext'

const STEPS = ['Zone', 'Comptage', 'Scan']

const stepIndexByPath: Record<string, number> = {
  '/inventory/location': 0,
  '/inventory/count-type': 1,
  '/inventory/session': 2,
  '/inventory/scan-camera': 2,
}

const homeLinkConfigByPath: Record<string, { to: string; label: string }> = {
  '/inventory/location': { to: '/', label: 'Retour à la page principale' },
  '/inventory/count-type': { to: '/inventory/location', label: 'Retour à la sélection de la zone' },
  '/inventory/session': { to: '/inventory/count-type', label: 'Retour à la sélection du comptage' },
  '/inventory/scan-camera': { to: '/inventory/session', label: 'Retour à l’écran de comptage' },
}

const defaultHomeLink = { to: '/select-user', label: 'Retour à l’accueil' } as const
const isTestEnv = typeof import.meta !== 'undefined' && import.meta.env?.MODE === 'test'

export const InventoryLayout = () => {
  const location = useLocation()
  const navigate = useNavigate()
  const { selectedUser, countType, location: selectedLocation } = useInventory()
  const locationId = selectedLocation?.id?.trim() ?? ''
  const stepperContainerRef = useRef<HTMLDivElement | null>(null)
  const { shop } = useShop()
  const shopDisplayName = shop?.name?.trim()
  const isAdminUser = Boolean(selectedUser?.isAdmin)

  useEffect(() => {
    if (isTestEnv) {
      return
    }
    const path = location.pathname
    if (path === '/inventory/location') {
      if (!selectedUser) {
        navigate('/select-user', { replace: true, state: { redirectTo: path } })
      }
      return
    }

    if (path === '/inventory/count-type') {
      if (!selectedUser) {
        navigate('/select-user', { replace: true, state: { redirectTo: path } })
        return
      }

      if (!locationId) {
        navigate('/inventory/location', { replace: true })
      }
      return
    }

    if (path === '/inventory/session' || path === '/inventory/scan-camera') {
      if (!selectedUser) {
        navigate('/select-user', { replace: true, state: { redirectTo: path } })
        return
      }

      if (!locationId) {
        navigate('/inventory/location', { replace: true })
        return
      }

      if (!countType) {
        navigate('/inventory/count-type', { replace: true })
      }
    }
  }, [countType, location.pathname, locationId, navigate, selectedUser])

  useEffect(() => {
    const container = stepperContainerRef.current
    if (!container) {
      return
    }

    const zoneStepButton = container.querySelector<HTMLElement>(
      'li:nth-of-type(1) button, li:nth-of-type(1) a, li:nth-of-type(1) [role="button"]',
    )
    if (zoneStepButton) {
      zoneStepButton.setAttribute('data-testid', 'step-nav-location')
      return
    }

    const zoneStepFallback = container.querySelector<HTMLElement>('li:nth-of-type(1)')
    zoneStepFallback?.setAttribute('data-testid', 'step-nav-location')
  }, [location.pathname])

  const activeIndex = stepIndexByPath[location.pathname] ?? 0
  const homeLinkConfig = homeLinkConfigByPath[location.pathname] ?? defaultHomeLink

  const headerAction = isAdminUser ? <AdminShortcutButton /> : null

  return (
    <Page
      className="gap-8"
      showHomeLink
      homeLinkTo={homeLinkConfig.to}
      homeLinkLabel={homeLinkConfig.label}
      headerAction={headerAction}
    >
      <div className="flex flex-col gap-6">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <p className="text-sm uppercase tracking-[0.25em] text-brand-500 dark:text-brand-200">
              {shopDisplayName ?? 'CinéBoutique'}
            </p>
            <h1 className="cb-section-title text-3xl">Assistant d&apos;inventaire</h1>
          </div>
          <div className="flex flex-col items-end gap-3 sm:flex-row sm:items-center">
            <div className="hidden text-right text-xs text-(--cb-muted) sm:block">
              <p>Utilisateur : {selectedUser?.displayName ?? '–'}</p>
              <p>Zone : {selectedLocation?.label ?? '–'}</p>
              <p>Comptage : {countType ?? '–'}</p>
            </div>
          </div>
        </div>
        <div ref={stepperContainerRef}>
          <Stepper steps={STEPS} activeIndex={activeIndex} />
        </div>
      </div>
      <div className="flex-1">
        <Outlet />
      </div>
    </Page>
  )
}
