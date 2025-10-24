import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type PointerEvent,
} from 'react'
import { useNavigate } from 'react-router-dom'
import clsx from 'clsx'
import { BrowserMultiFormatReader } from '@zxing/browser'
import { BarcodeFormat, DecodeHintType, NotFoundException } from '@zxing/library'
import { BarcodeScanner } from '../../components/BarcodeScanner'
import { useInventory } from '../../contexts/InventoryContext'
import { useShop } from '@/state/ShopContext'
import { Button } from '../../components/ui/Button'
import { ScannedRow, type ScannedRowHandle } from '../../components/inventory/ScannedRow'
import { ConflictZoneModal } from '../../components/Conflicts/ConflictZoneModal'
import { CountType, type ConflictZoneSummary, type Product } from '../../types/inventory'
import { fetchProductByEan, startInventoryRun } from '../../api/inventoryApi'
import type { HttpError } from '@/lib/api/http'
import { ProductsListCompact } from '@/components/products/ProductsListCompact'
import { useScanRejectionFeedback } from '@/hooks/useScanRejectionFeedback'

const MAX_SCAN_LENGTH = 32

type SheetState = 'closed' | 'half' | 'full'

const SHEET_HEIGHTS: Record<SheetState, string> = {
  closed: '130px',
  half: '62vh',
  full: '90vh',
}

const sanitizeEan = (value: string) => value.replace(/\D+/g, '')

const isScanLengthValid = (code: string) => code.length > 0 && code.length <= MAX_SCAN_LENGTH

const moveState = (current: SheetState, direction: 'up' | 'down'): SheetState => {
  if (direction === 'up') {
    if (current === 'closed') return 'half'
    if (current === 'half') return 'full'
    return 'full'
  }
  if (current === 'full') return 'half'
  if (current === 'half') return 'closed'
  return 'closed'
}

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
  const [sheetState, setSheetState] = useState<SheetState>('closed')
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [statusMessage, setStatusMessage] = useState<string | null>(null)
  const [highlightEan, setHighlightEan] = useState<string | null>(null)
  const [conflictModalOpen, setConflictModalOpen] = useState(false)
  const triggerScanRejectionFeedback = useScanRejectionFeedback()
  const shopId = shop?.id?.trim() ?? ''
  const dragStateRef = useRef<{ startY: number; pointerId: number } | null>(null)
  const manualInputActiveRef = useRef(false)
  const focusedRowKeyRef = useRef<string | null>(null)
  const scrollToEndRef = useRef(false)
  const listContainerRef = useRef<HTMLDivElement | null>(null)
  const fallbackReaderRef = useRef<BrowserMultiFormatReader | null>(null)
  const rowRefs = useRef<Map<string, ScannedRowHandle>>(new Map())
  const highlightTimeoutRef = useRef<number | null>(null)
  const pendingFocusEanRef = useRef<string | null>(null)
  const shopName = shop?.name ?? 'Boutique'
  const ownerUserId = selectedUser?.id?.trim() ?? ''
  const locationId = location?.id?.trim() ?? ''
  const countTypeValue = typeof countType === 'number' ? countType : null
  const sessionRunId = typeof sessionId === 'string' ? sessionId.trim() : ''

  const conflictZoneSummary = useMemo<ConflictZoneSummary | null>(() => {
    if (typeof countType !== 'number' || countType < CountType.Count3 || !location) {
      return null
    }
    return {
      locationId: location.id,
      locationCode: location.code,
      locationLabel: location.label,
      conflictLines: 0,
    }
  }, [countType, location])

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
    if (!scrollToEndRef.current) {
      return
    }
    const container = listContainerRef.current
    if (container) {
      container.scrollTo({ top: container.scrollHeight, behavior: 'smooth' })
    }
    scrollToEndRef.current = false
  }, [items.length])

  useEffect(() => {
    if (!highlightEan) {
      return
    }
    if (highlightTimeoutRef.current) {
      window.clearTimeout(highlightTimeoutRef.current)
    }
    highlightTimeoutRef.current = window.setTimeout(() => {
      setHighlightEan(null)
    }, 600)
    return () => {
      if (highlightTimeoutRef.current) {
        window.clearTimeout(highlightTimeoutRef.current)
      }
    }
  }, [highlightEan])

  useEffect(() => {
    const targetEan = pendingFocusEanRef.current
    if (!targetEan) {
      return
    }
    const handle = rowRefs.current.get(targetEan)
    if (handle) {
      requestAnimationFrame(() => {
        handle.focusQuantity()
      })
      pendingFocusEanRef.current = null
    }
  }, [items])

  const totalQuantity = useMemo(
    () => items.reduce((acc, item) => acc + item.quantity, 0),
    [items],
  )

  const orderedItems = useMemo(() => [...items].reverse(), [items])
  const closedItems = useMemo(() => orderedItems.slice(-3), [orderedItems])

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
    if (items.length > 0) {
      return sessionRunId || null
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
    async (product: Product, options?: { isManual?: boolean }) => {
      try {
        await ensureActiveRun()
      } catch (error) {
        setStatusMessage(null)
        setErrorMessage(error instanceof Error ? error.message : 'Impossible de démarrer le comptage.')
        return false
      }
      addOrIncrementItem(product, options)
      return true
    },
    [addOrIncrementItem, ensureActiveRun],
  )

  const handleDetected = useCallback(
    async (rawValue: string) => {
      const sanitized = sanitizeEan(rawValue.trim())
      if (!sanitized) {
        return
      }
      if (!isScanLengthValid(sanitized)) {
        setErrorMessage(`Code ${sanitized} invalide : ${MAX_SCAN_LENGTH} chiffres maximum.`)
        setStatusMessage(null)
        return
      }
      try {
        ensureScanPrerequisites()
      } catch (error) {
        setErrorMessage(error instanceof Error ? error.message : 'Impossible de lancer le scan.')
        setStatusMessage(null)
        return
      }
      setStatusMessage(`Lecture de ${sanitized}…`)
      setErrorMessage(null)
      try {
        const product = await fetchProductByEan(sanitized)
        const added = await addProductToSession(product)
        if (added) {
          setStatusMessage(`${product.name} ajouté`)
          setHighlightEan(product.ean)
          scrollToEndRef.current = true
          if (manualInputActiveRef.current) {
            pendingFocusEanRef.current = product.ean
          }
          // TODO: jouer un son de confirmation court si disponible.
        }
      } catch (error) {
        const err = error as HttpError
        if (err?.status === 404) {
          setErrorMessage(`Code ${sanitized} introuvable dans l’inventaire. Signalez-le pour création.`)
          triggerScanRejectionFeedback()
        } else {
          setErrorMessage('Échec de la récupération du produit. Réessayez.')
        }
        setStatusMessage(null)
      }
    [addProductToSession, ensureScanPrerequisites, triggerScanRejectionFeedback],
  )

  const handlePickFromCatalogue = useCallback(
    async ({ sku, name, ean }: { sku: string; name: string; ean?: string | null }) => {
      const sanitizedEan = sanitizeEan(ean ?? '')
      if (!sanitizedEan) {
        setErrorMessage(`Impossible d’ajouter ${name} : code manquant.`)
        setStatusMessage(null)
        return
      }
      if (!isScanLengthValid(sanitizedEan)) {
        setErrorMessage(`Code ${sanitizedEan} invalide : ${MAX_SCAN_LENGTH} chiffres maximum.`)
        setStatusMessage(null)
        return
      }
      try {
        ensureScanPrerequisites()
      } catch (error) {
        setErrorMessage(error instanceof Error ? error.message : 'Impossible d’ajouter ce produit.')
        setStatusMessage(null)
        return
      }

      setStatusMessage(`Ajout de ${name}…`)
      setErrorMessage(null)
      try {
        const product = await fetchProductByEan(sanitizedEan)
        const added = await addProductToSession({ ...product, sku: product.sku ?? sku })
        if (!added) {
          setStatusMessage(null)
          return
        }
        setStatusMessage(`${product.name} ajouté`)
        setHighlightEan(product.ean)
        scrollToEndRef.current = true
        pendingFocusEanRef.current = product.ean
        setSheetState('full')
      } catch (error) {
        const err = error as HttpError
        if (err?.status === 404) {
          setErrorMessage(`Produit introuvable pour ${sanitizedEan}. Signalez ce code.`)
          triggerScanRejectionFeedback()
        } else {
          setErrorMessage('Impossible d’ajouter ce produit. Réessayez.')
        }
        setStatusMessage(null)
      }
    },
    [addProductToSession, ensureScanPrerequisites, triggerScanRejectionFeedback],
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

  const registerRowRef = useCallback((key: string) => {
    return (instance: ScannedRowHandle | null) => {
      if (!key) {
        return
      }
      if (!instance) {
        rowRefs.current.delete(key)
      } else {
        rowRefs.current.set(key, instance)
      }
    }
  }, [])

  const handleQuantityFocusChange = useCallback((focused: boolean, rowKey: string) => {
    manualInputActiveRef.current = focused
    if (focused) {
      focusedRowKeyRef.current = rowKey
      setSheetState('full')
      requestAnimationFrame(() => {
        const handle = rowRefs.current.get(rowKey)
        handle?.scrollIntoView({ block: 'center', behavior: 'smooth' })
      })
      return
    }
    if (focusedRowKeyRef.current === rowKey) {
      focusedRowKeyRef.current = null
    }
  }, [])

  const getFallbackReader = useCallback(() => {
    if (!fallbackReaderRef.current) {
      const hints = new Map<DecodeHintType, unknown>()
      hints.set(DecodeHintType.POSSIBLE_FORMATS, [
        BarcodeFormat.EAN_13,
        BarcodeFormat.EAN_8,
        BarcodeFormat.CODE_128,
        BarcodeFormat.CODE_39,
        BarcodeFormat.ITF,
        BarcodeFormat.QR_CODE,
      ])
      hints.set(DecodeHintType.TRY_HARDER, true)
      fallbackReaderRef.current = new BrowserMultiFormatReader(hints)
    }
    return fallbackReaderRef.current
  }, [])

  const handleImageImport = useCallback(
    async (file: File) => {
      const reader = getFallbackReader()
      if (!reader) {
        return
      }

      setErrorMessage(null)
      setStatusMessage('Analyse de la photo…')

      const objectUrl = URL.createObjectURL(file)
      try {
        const result = await reader.decodeFromImageUrl(objectUrl)
        const text = result?.getText?.()
        if (text) {
          await handleDetected(text)
          return
        }
        setStatusMessage(null)
        setErrorMessage('Aucun code-barres détecté sur cette image.')
      } catch (error) {
        setStatusMessage(null)
        if (error instanceof NotFoundException) {
          setErrorMessage('Aucun code-barres détecté sur cette image.')
        } else {
          setErrorMessage('Impossible de lire le code-barres sur cette image.')
        }
      } finally {
        URL.revokeObjectURL(objectUrl)
      }
    },
    [getFallbackReader, handleDetected],
  )

  const handleDragStart = useCallback(
    (event: PointerEvent<HTMLDivElement>) => {
      dragStateRef.current = { startY: event.clientY, pointerId: event.pointerId }
      event.currentTarget.setPointerCapture?.(event.pointerId)
    },
    [],
  )

  const handleDragEnd = useCallback(
    (event: PointerEvent<HTMLDivElement>) => {
      const snapshot = dragStateRef.current
      dragStateRef.current = null
      if (!snapshot) {
        return
      }
      const deltaY = event.clientY - snapshot.startY
      const threshold = 40
      if (deltaY <= -threshold) {
        setSheetState((prev) => moveState(prev, 'up'))
      } else if (deltaY >= threshold) {
        setSheetState((prev) => moveState(prev, 'down'))
      }
      event.currentTarget.releasePointerCapture?.(snapshot.pointerId)
    },
    [],
  )

  const handleToggleSheet = useCallback(() => {
    setSheetState((prev) => (prev === 'closed' ? 'half' : prev === 'half' ? 'full' : 'closed'))
  }, [])

  const sheetHeight = SHEET_HEIGHTS[sheetState]
  const displayedItems = sheetState === 'closed' ? closedItems : orderedItems

  return (
    <div className="relative flex h-full flex-col bg-black text-white" data-testid="scan-camera-page">
      <div className="relative flex-1 overflow-hidden">
        <BarcodeScanner
          active
          onDetected={handleDetected}
          onPickImage={handleImageImport}
          enableTorchToggle
          presentation="immersive"
        />
        {statusMessage && (
          <div className="absolute inset-x-0 top-0 z-10 flex justify-center p-3">
            <span className="rounded-full bg-black/60 px-3 py-1 text-xs font-semibold text-emerald-200 backdrop-blur">
              {statusMessage}
            </span>
          </div>
        )}
        <div className="pointer-events-none absolute inset-0 bg-gradient-to-t from-black/70 via-transparent to-black/70" />
      </div>
      <div className="absolute left-0 right-0 top-0 z-20 flex items-center justify-between px-4 py-3">
        <div>
          <p className="text-xs uppercase tracking-[0.3em] text-white/70">{shopName}</p>
          <p className="text-lg font-semibold">{location?.label ?? 'Zone inconnue'}</p>
        </div>
        <div className="flex items-center gap-3">
          <div className="rounded-full bg-white/20 px-3 py-1 text-sm font-semibold">
            {totalQuantity} pièces
          </div>
          <Button
            type="button"
            variant="secondary"
            className="bg-white/80 text-slate-900 hover:bg-white"
            onClick={() => navigate('/inventory/session')}
          >
            Stop scan
          </Button>
        </div>
      </div>
      <div
        className={clsx(
          'absolute inset-x-0 bottom-0 z-30 flex flex-col rounded-t-3xl bg-white text-slate-900 shadow-2xl transition-[height]',
          'relative overflow-hidden',
          'dark:bg-slate-900 dark:text-white',
        )}
        style={{ height: sheetHeight }}
        data-state={sheetState}
        data-testid="scan-sheet"
      >
        {sheetState === 'closed' && (
          <div className="pointer-events-none absolute inset-x-0 top-0 h-10 rounded-t-3xl bg-gradient-to-b from-slate-200/80 via-white/70 to-transparent dark:from-slate-800/80 dark:via-slate-900/70" />
        )}
        <div
          className="flex flex-col px-4 pt-3"
          onPointerDown={handleDragStart}
          onPointerUp={handleDragEnd}
          role="presentation"
          data-testid="scan-sheet-handle"
        >
          <button
            type="button"
            className="mx-auto mb-3 h-1.5 w-16 rounded-full bg-slate-300"
            onClick={handleToggleSheet}
            aria-label="Changer la hauteur du panneau"
          />
          <div className="flex items-center justify-between pb-2">
            <h2 className="text-base font-semibold">Articles scannés</h2>
            <span className="text-xs text-slate-500">{items.length} références</span>
          </div>
        </div>
        <div className="relative flex-1 overflow-hidden">
          {sheetState === 'closed' && (
            <div className="pointer-events-none absolute inset-x-0 top-0 h-5 bg-gradient-to-b from-white via-white/80 to-transparent dark:from-slate-900 dark:via-slate-900/80" />
          )}
          <div
            ref={listContainerRef}
            className={clsx(
              'h-full space-y-2 overflow-y-auto px-4 pb-6',
              sheetState === 'closed' ? 'pointer-events-auto' : null,
            )}
          >
            {displayedItems.length === 0 ? (
              <p className="text-sm text-slate-500">Scannez un article pour débuter.</p>
            ) : (
              <ul className={clsx('flex flex-col', sheetState === 'closed' ? 'gap-1.5' : 'gap-2')}>
                {displayedItems.map((item) => {
                  const hasConflict = Boolean(item.hasConflict)
                  const rowKey = item.product.ean ?? item.id
                  return (
                    <ScannedRow
                      key={item.id}
                      ref={registerRowRef(rowKey)}
                      id={item.id}
                      ean={item.product.ean}
                      label={item.product.name}
                      sku={item.product.sku}
                      qty={item.quantity}
                      highlight={highlightEan === item.product.ean}
                      hasConflict={hasConflict}
                      density={sheetState === 'closed' ? 'dense' : 'regular'}
                      onInc={() => handleInc(item.product.ean, item.quantity)}
                      onDec={() => handleDec(item.product.ean, item.quantity)}
                      onSetQty={(next) => handleSetQuantity(item.product.ean, next)}
                      onOpenConflict={() => setConflictModalOpen(true)}
                      onQuantityFocusChange={handleQuantityFocusChange}
                    />
                  )
                })}
              </ul>
            )}
            {shopId && (
              <div className="mt-6 border-t border-slate-200 pt-4 dark:border-slate-700">
                <ProductsListCompact shopId={shopId} onPick={handlePickFromCatalogue} />
              </div>
            )}
          </div>
        </div>
      </div>
      {errorMessage && (
        <div className="absolute inset-x-0 bottom-[calc(100%+8px)] z-30 flex justify-center p-3">
          <div className="flex items-center gap-3 rounded-full bg-rose-600 px-4 py-2 text-sm font-semibold text-white shadow-lg">
            <span>{errorMessage}</span>
          </div>
        </div>
      )}
      {conflictZoneSummary && (
        <ConflictZoneModal
          open={conflictModalOpen}
          zone={conflictZoneSummary}
          onClose={() => setConflictModalOpen(false)}
        />
      )}
    </div>
  )
}

export default ScanCameraPage

