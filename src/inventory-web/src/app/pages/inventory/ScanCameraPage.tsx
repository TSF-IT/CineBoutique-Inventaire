import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'

import { fetchProductByEan, startInventoryRun } from '../../api/inventoryApi'
import { BarcodeScanner } from '../../components/BarcodeScanner'
import { ScannedRow } from '../../components/inventory/ScannedRow'
import { Button } from '../../components/ui/Button'
import { useInventory } from '../../contexts/InventoryContext'
import type { Product } from '../../types/inventory'

import { useScanRejectionFeedback } from '@/hooks/useScanRejectionFeedback'
import { useCamera } from '@/hooks/useCamera'
import type { HttpError } from '@/lib/api/http'
import { useShop } from '@/state/ShopContext'

const MAX_SCAN_LENGTH = 32
const LOCK_RELEASE_DELAY = 700

const sanitizeScanValue = (value: string) => value.replace(/\r|\n/g, '')

const isScanLengthValid = (code: string) => code.length > 0 && code.length <= MAX_SCAN_LENGTH

export const ScanCameraPage = () => {
  const navigate = useNavigate()
  const { shop } = useShop()
  const {
    selectedUser,
    location,
    countType,
    items,
    addOrIncrementItem,
    setQuantity,
    removeItem,
    sessionId,
    setSessionId,
  } = useInventory()
  const [statusMessage, setStatusMessage] = useState<string | null>(null)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [highlightEan, setHighlightEan] = useState<string | null>(null)
  const highlightTimeoutRef = useRef<number | null>(null)
  const statusTimeoutRef = useRef<number | null>(null)
  const lockTimeoutRef = useRef<number | null>(null)
  const lockedEanRef = useRef<string | null>(null)
  const triggerScanRejectionFeedback = useScanRejectionFeedback()
  const videoRef = useRef<HTMLVideoElement | null>(null)
  const {
    active: cameraActive,
    error: cameraError,
    stop: stopCamera,
  } = useCamera(videoRef.current, {
    constraints: { video: { facingMode: { ideal: 'environment' } }, audio: false },
    autoResumeOnVisible: true,
  })

  const shopName = shop?.name ?? 'Boutique'
  const ownerUserId = selectedUser?.id?.trim() ?? ''
  const locationId = location?.id?.trim() ?? ''
  const countTypeValue = typeof countType === 'number' ? countType : null
  const sessionRunId = typeof sessionId === 'string' ? sessionId.trim() : ''

  useEffect(() => {
    if (!selectedUser) {
      navigate('/select-shop', { replace: true })
      return
    }
    if (!locationId) {
      navigate('/inventory/location', { replace: true })
      return
    }
    if (!countTypeValue) {
      navigate('/inventory/count-type', { replace: true })
    }
  }, [countTypeValue, locationId, navigate, selectedUser])

  useEffect(() => {
    return () => {
      stopCamera()
      if (highlightTimeoutRef.current) {
        window.clearTimeout(highlightTimeoutRef.current)
        highlightTimeoutRef.current = null
      }
      if (statusTimeoutRef.current) {
        window.clearTimeout(statusTimeoutRef.current)
        statusTimeoutRef.current = null
      }
      if (lockTimeoutRef.current) {
        window.clearTimeout(lockTimeoutRef.current)
        lockTimeoutRef.current = null
      }
      lockedEanRef.current = null
    }
  }, [stopCamera])

  useEffect(() => {
    if (!highlightEan) {
      return
    }
    if (highlightTimeoutRef.current) {
      window.clearTimeout(highlightTimeoutRef.current)
    }
    highlightTimeoutRef.current = window.setTimeout(() => {
      setHighlightEan(null)
      highlightTimeoutRef.current = null
    }, 700)
  }, [highlightEan])

  useEffect(() => {
    if (!statusMessage) {
      return
    }
    if (statusTimeoutRef.current) {
      window.clearTimeout(statusTimeoutRef.current)
    }
    statusTimeoutRef.current = window.setTimeout(() => {
      setStatusMessage(null)
      statusTimeoutRef.current = null
    }, 2200)
  }, [statusMessage])

  const totalQuantity = useMemo(
    () => items.reduce((acc, item) => acc + item.quantity, 0),
    [items],
  )

  const orderedItems = useMemo(() => [...items].reverse(), [items])

  const ensureScanPrerequisites = useCallback(() => {
    if (!shop?.id) {
      throw new Error('Sélectionnez une boutique valide avant de scanner un produit.')
    }
    if (!ownerUserId) {
      throw new Error('Sélectionnez un utilisateur avant de scanner un produit.')
    }
    if (!locationId) {
      throw new Error('Sélectionnez une zone avant de scanner un produit.')
    }
    if (!countTypeValue) {
      throw new Error('Choisissez un type de comptage avant de scanner un produit.')
    }
  }, [countTypeValue, locationId, ownerUserId, shop?.id])

  const ensureActiveRun = useCallback(async () => {
    if (items.length > 0 && sessionRunId) {
      return sessionRunId
    }
    if (sessionRunId) {
      return sessionRunId
    }
    ensureScanPrerequisites()
    const response = await startInventoryRun(locationId, {
      shopId: shop!.id,
      ownerUserId,
      countType: countTypeValue!,
    })
    const nextRunId = typeof response.runId === 'string' ? response.runId.trim() : ''
    if (nextRunId) {
      setSessionId(nextRunId)
      return nextRunId
    }
    return null
  }, [countTypeValue, ensureScanPrerequisites, items.length, locationId, ownerUserId, sessionRunId, setSessionId, shop])

  const addProductToSession = useCallback(
    async (product: Product) => {
      try {
        await ensureActiveRun()
      } catch (error) {
        setStatusMessage(null)
        setErrorMessage(error instanceof Error ? error.message : 'Impossible de démarrer le comptage.')
        return false
      }
      addOrIncrementItem(product)
      return true
    },
    [addOrIncrementItem, ensureActiveRun],
  )

  const armScanLock = useCallback(
    (ean: string | null) => {
      if (lockTimeoutRef.current) {
        window.clearTimeout(lockTimeoutRef.current)
        lockTimeoutRef.current = null
      }
      lockedEanRef.current = ean
      if (!ean) {
        return
      }
      lockTimeoutRef.current = window.setTimeout(() => {
        lockedEanRef.current = null
        lockTimeoutRef.current = null
      }, LOCK_RELEASE_DELAY)
    },
    [],
  )

  const refreshScanLock = useCallback(() => {
    const current = lockedEanRef.current
    if (!current) {
      return
    }
    armScanLock(current)
  }, [armScanLock])

  const handleProductAdded = useCallback((product: Product) => {
    setStatusMessage(`${product.name} ajouté`)
    const normalizedEan = product.ean?.trim() ?? null
    if (normalizedEan) {
      setHighlightEan(normalizedEan)
    } else {
      setHighlightEan(null)
    }
  }, [])

  const handleDetected = useCallback(
    async (rawValue: string) => {
      const sanitized = sanitizeScanValue(rawValue)
      if (!sanitized) {
        return
      }

      if (lockedEanRef.current && lockedEanRef.current === sanitized) {
        refreshScanLock()
        return
      }

      if (!isScanLengthValid(sanitized)) {
        setErrorMessage(`Code ${sanitized} invalide : ${MAX_SCAN_LENGTH} caractères maximum.`)
        setStatusMessage(null)
        armScanLock(sanitized)
        return
      }

      try {
        ensureScanPrerequisites()
      } catch (error) {
        setErrorMessage(error instanceof Error ? error.message : 'Impossible de lancer le scan.')
        setStatusMessage(null)
        armScanLock(null)
        return
      }

      setStatusMessage(`Lecture de ${sanitized}…`)
      setErrorMessage(null)
      armScanLock(sanitized)

      try {
        const product = await fetchProductByEan(sanitized)
        const added = await addProductToSession(product)
        if (added) {
          handleProductAdded(product)
        }
      } catch (error) {
        const err = error as HttpError
        if (err?.status === 404) {
          setErrorMessage(`Code ${sanitized} introuvable dans la liste des produits à inventorier.`)
          triggerScanRejectionFeedback()
        } else {
          setErrorMessage('Échec de la récupération du produit. Réessayez.')
        }
        setStatusMessage(null)
      } finally {
        armScanLock(sanitized)
      }
    },
    [addProductToSession, armScanLock, ensureScanPrerequisites, handleProductAdded, refreshScanLock, triggerScanRejectionFeedback],
  )

  const handleDec = useCallback(
    (ean: string, quantity: number) => {
      if (quantity <= 1) {
        removeItem(ean)
        return
      }
      setQuantity(ean, quantity - 1)
    },
    [removeItem, setQuantity],
  )

  const handleInc = useCallback(
    (ean: string, quantity: number) => {
      setQuantity(ean, quantity + 1)
    },
    [setQuantity],
  )

  const handleSetQuantity = useCallback(
    (ean: string, next: number | null) => {
      if (next === null) {
        return
      }
      if (next <= 0) {
        removeItem(ean)
        return
      }
      setQuantity(ean, next)
    },
    [removeItem, setQuantity],
  )

  return (
    <div className="flex h-full flex-col bg-black text-white" data-testid="scan-camera-page">
      <div className="relative flex-none min-h-[52vh] w-full overflow-hidden">
        <BarcodeScanner
          active
          onDetected={handleDetected}
          presentation="immersive"
          enableTorchToggle
          camera={{ videoRef, active: cameraActive, error: cameraError }}
        />
        <div className="pointer-events-none absolute inset-x-0 top-6 flex justify-center">
          {!cameraActive && !cameraError && (
            <span className="rounded-full bg-black/60 px-3 py-1 text-sm font-semibold text-white backdrop-blur">
              Démarrage caméra…
            </span>
          )}
          {cameraError && (
            <span className="rounded-full bg-black/60 px-3 py-1 text-sm font-semibold text-rose-200 backdrop-blur">
              Caméra indisponible : {String((cameraError as any)?.name || cameraError)}
            </span>
          )}
        </div>
        <div className="absolute left-4 top-4 flex items-center gap-3">
          <Button
            size="sm"
            variant="ghost"
            className="bg-black/60 px-4 text-white hover:bg-black/40"
            onClick={() => navigate('/inventory/session')}
          >
            Retour
          </Button>
        </div>
      </div>
      <div className="flex min-h-0 flex-1 flex-col rounded-t-[28px] bg-white text-slate-900 shadow-[0_-16px_40px_-32px_rgba(15,23,42,0.45)] dark:bg-slate-950 dark:text-white">
        <div className="flex items-center justify-between gap-3 px-5 pb-3 pt-4">
          <div className="flex min-w-0 flex-col">
            <span className="text-[11px] uppercase tracking-[0.28em] text-slate-400 dark:text-slate-500">
              {shopName}
            </span>
            <span className="mt-1 truncate text-base font-semibold leading-tight text-slate-900 dark:text-white">
              {location?.label ?? 'Zone inconnue'}
            </span>
          </div>
          <span className="shrink-0 rounded-full bg-slate-900 px-3 py-1 text-sm font-semibold text-white dark:bg-slate-700">
            {totalQuantity} pièce{totalQuantity > 1 ? 's' : ''}
          </span>
        </div>
        <div className="space-y-2 px-5">
          {statusMessage && (
            <div className="rounded-2xl bg-emerald-50 px-3 py-2 text-xs font-semibold text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-200">
              {statusMessage}
            </div>
          )}
          {errorMessage && (
            <div className="rounded-2xl bg-rose-50 px-3 py-2 text-xs font-semibold text-rose-600 dark:bg-rose-500/10 dark:text-rose-200">
              {errorMessage}
            </div>
          )}
        </div>
        <div className="flex-1 overflow-y-auto px-5 pb-8 pt-4">
          {orderedItems.length === 0 ? (
            <p className="text-sm text-slate-500 dark:text-slate-400">
              Scannez un article pour commencer le comptage.
            </p>
          ) : (
            <ul className="space-y-3">
              {orderedItems.map((item) => {
                const ean = item.product.ean
                return (
                  <ScannedRow
                    key={item.id}
                    id={item.id}
                    ean={item.product.ean}
                    label={item.product.name}
                    sku={item.product.sku}
                    subGroup={item.product.subGroup}
                    qty={item.quantity}
                    highlight={highlightEan === item.product.ean}
                    hasConflict={Boolean(item.hasConflict)}
                    onInc={() => handleInc(ean, item.quantity)}
                    onDec={() => handleDec(ean, item.quantity)}
                    onSetQty={(value) => handleSetQuantity(ean, value)}
                  />
                )
              })}
            </ul>
          )}
        </div>
      </div>
    </div>
  )
}

export default ScanCameraPage
