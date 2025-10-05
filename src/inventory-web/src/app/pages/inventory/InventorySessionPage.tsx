// Modifications : forcer l'inclusion de runId=null lors de la complétion sans run existant.
import type { KeyboardEvent, ChangeEvent } from 'react'
import { useCallback, useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { BrowserMultiFormatReader } from '@zxing/browser'
import { BarcodeFormat, DecodeHintType } from '@zxing/library'
import {
  completeInventoryRun,
  fetchProductByEan,
  releaseInventoryRun,
  startInventoryRun,
  type CompleteInventoryRunPayload,
} from '../../api/inventoryApi'
import { BarcodeScanner } from '../../components/BarcodeScanner'
import { Button } from '../../components/ui/Button'
import { Input } from '../../components/ui/Input'
import { Card } from '../../components/Card'
import { EmptyState } from '../../components/EmptyState'
import { useInventory } from '../../contexts/InventoryContext'
import type { HttpError } from '@/lib/api/http'
import type { Product } from '../../types/inventory'
import { CountType } from '../../types/inventory'
import { useShop } from '@/state/ShopContext'

const DEV_API_UNREACHABLE_HINT =
  "Impossible de joindre l’API : vérifie que le backend tourne (curl http://localhost:8080/healthz) ou que le proxy Vite est actif."

const isHttpError = (value: unknown): value is HttpError =>
  typeof value === 'object' &&
  value !== null &&
  typeof (value as { status?: unknown }).status === 'number' &&
  typeof (value as { url?: unknown }).url === 'string'

const buildHttpMessage = (prefix: string, error: HttpError) => {
  const problem = error.problem as { detail?: string; title?: string; message?: string } | undefined
  const detail = problem?.detail ?? problem?.title ?? problem?.message ?? error.body
  const trimmedDetail = detail?.trim()

  if (import.meta.env.DEV && error.status === 404) {
    const looksLikeProxyNotFound = !trimmedDetail || /^not found$/i.test(trimmedDetail)
    if (looksLikeProxyNotFound) {
      const diagnostics = [DEV_API_UNREACHABLE_HINT]
      if (error.url) {
        diagnostics.push(`URL: ${error.url}`)
      }
      if (trimmedDetail) {
        diagnostics.push(`Détail: ${trimmedDetail}`)
      }
      return diagnostics.join(' | ')
    }
  }

  const diagnostics: string[] = []
  if (typeof error.status === 'number') {
    diagnostics.push(`HTTP ${error.status}`)
  }
  if (error.url) {
    diagnostics.push(`URL: ${error.url}`)
  }
  if (trimmedDetail) {
    diagnostics.push(`Détail: ${trimmedDetail}`)
  }
  return diagnostics.length > 0 ? `${prefix} | ${diagnostics.join(' | ')}` : prefix
}

const hasBarcodeDetector = (
  candidate: Window & typeof globalThis,
): candidate is Window & typeof globalThis & { BarcodeDetector: BarcodeDetectorConstructor } =>
  'BarcodeDetector' in candidate && typeof candidate.BarcodeDetector === 'function'

const ZXING_IMAGE_HINTS = (() => {
  const hints = new Map()
  hints.set(DecodeHintType.POSSIBLE_FORMATS, [
    BarcodeFormat.EAN_13,
    BarcodeFormat.EAN_8,
    BarcodeFormat.CODE_128,
    BarcodeFormat.CODE_39,
    BarcodeFormat.ITF,
    BarcodeFormat.QR_CODE,
  ])
  hints.set(DecodeHintType.TRY_HARDER, true)
  return hints
})()

const resolveLifecycleErrorMessage = (error: unknown, fallback: string): string => {
  if (isHttpError(error)) {
    const problem = error.problem as { message?: unknown; detail?: unknown; title?: unknown } | undefined
    const candidates = [problem?.message, problem?.detail, problem?.title]
    const message = candidates.find(
      (value): value is string => typeof value === 'string' && value.trim().length > 0,
    )
    if (message) {
      return message.trim()
    }
    return buildHttpMessage(fallback, error)
  }

  if (error instanceof Error) {
    const trimmed = error.message.trim()
    if (trimmed.length > 0) {
      return trimmed
    }
  }

  return fallback
}

export const InventorySessionPage = () => {
  const navigate = useNavigate()
  const {
    selectedUser,
    countType,
    location,
    items,
    addOrIncrementItem,
    setQuantity,
    removeItem,
    sessionId,
    setSessionId,
    clearSession,
  } = useInventory()
  const { shop } = useShop()
  const [useCamera, setUseCamera] = useState(false)
  const [status, setStatus] = useState<string | null>(null)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [scanValue, setScanValue] = useState('')
  const [manualEan, setManualEan] = useState('')
  const [inputLookupStatus, setInputLookupStatus] = useState<'idle' | 'loading' | 'found' | 'not-found' | 'error'>('idle')
  const [completionLoading, setCompletionLoading] = useState(false)
  const completionDialogRef = useRef<HTMLDialogElement | null>(null)
  const completionConfirmationDialogRef = useRef<HTMLDialogElement | null>(null)
  const completionOkButtonRef = useRef<HTMLButtonElement | null>(null)
  const completionConfirmButtonRef = useRef<HTMLButtonElement | null>(null)
  const inputRef = useRef<HTMLInputElement | null>(null)
  const [recentScans, setRecentScans] = useState<string[]>([])
  const manualLookupIdRef = useRef(0)
  const lastSearchedInputRef = useRef<string | null>(null)
  const previousItemCountRef = useRef(items.length)

  const selectedUserDisplayName = selectedUser?.displayName ?? null
  const ownerUserId = selectedUser?.id?.trim() ?? ''
  const existingRunId = typeof sessionId === 'string' ? sessionId.trim() : ''
  const locationId = location?.id?.trim() ?? ''
  const shopId = shop?.id?.trim() ?? ''

  useEffect(() => {
    if (!selectedUser) {
      navigate('/inventory/start', { replace: true })
    } else if (!locationId) {
      navigate('/inventory/location', { replace: true })
    } else if (!countType) {
      navigate('/inventory/count-type', { replace: true })
    }
  }, [countType, locationId, navigate, selectedUser])

  const displayedItems = items

  const isValidCountType =
    countType === CountType.Count1 || countType === CountType.Count2 || countType === CountType.Count3

  const ensureScanPrerequisites = useCallback(() => {
    if (!shopId) {
      throw new Error('Sélectionnez une boutique valide avant de scanner un produit.')
    }

    if (!ownerUserId) {
      throw new Error('Sélectionnez un utilisateur avant de scanner un produit.')
    }

    if (!locationId) {
      throw new Error('Sélectionnez une zone avant de scanner un produit.')
    }

    if (!isValidCountType) {
      throw new Error('Choisissez un type de comptage avant de scanner un produit.')
    }
  }, [isValidCountType, locationId, ownerUserId, shopId])

  const searchProductByEan = useCallback(
    async (ean: string) => {
      try {
        ensureScanPrerequisites()
      } catch (error) {
        return { status: 'error' as const, error }
      }

      try {
        const product = await fetchProductByEan(ean)
        return { status: 'found' as const, product }
      } catch (error) {
        const err = error as HttpError
        if (isHttpError(err) && err.status === 404) {
          console.warn('[inventory] produit introuvable ignoré', err)
          return { status: 'not-found' as const, error: err }
        }
        console.error('[inventory] échec lecture produit', err)
        return { status: 'error' as const, error: err }
      }
    },
    [ensureScanPrerequisites],
  )

  const ensureActiveRun = useCallback(async () => {
    if (items.length > 0) {
      return existingRunId || null
    }

    if (existingRunId) {
      return existingRunId
    }

    if (!locationId) {
      throw new Error('Sélectionnez une zone avant de scanner.')
    }

    if (!ownerUserId) {
      throw new Error("Sélectionnez un utilisateur avant de démarrer le comptage.")
    }

    if (!shopId) {
      throw new Error('Sélectionnez une boutique valide avant de démarrer le comptage.')
    }

    if (!isValidCountType) {
      throw new Error('Le type de comptage est invalide.')
    }

    const response = await startInventoryRun(locationId, {
      shopId,
      ownerUserId,
      countType: countType as 1 | 2 | 3,
    })

    const sanitizedRunId = typeof response.runId === 'string' ? response.runId.trim() : ''
    if (sanitizedRunId) {
      setSessionId(sanitizedRunId)
      return sanitizedRunId
    }

    return null
  }, [
    countType,
    existingRunId,
    isValidCountType,
    items.length,
    locationId,
    ownerUserId,
    setSessionId,
    shopId,
  ])

  const addProductToSession = useCallback(
    async (product: Product, options?: { isManual?: boolean }) => {
      if (items.length === 0) {
        try {
          await ensureActiveRun()
        } catch (error) {
          const message = resolveLifecycleErrorMessage(error, 'Impossible de démarrer le comptage.')
          setStatus(null)
          setErrorMessage(message)
          return false
        }
      }

      setErrorMessage(null)
      addOrIncrementItem(product, options)
      return true
    },
    [addOrIncrementItem, ensureActiveRun, items.length, setErrorMessage, setStatus],
  )

  const handleDetected = useCallback(
    async (rawValue: string) => {
      const value = rawValue.trim()
      if (!value) {
        return
      }
      setStatus(`Recherche du code ${value}`)
      setErrorMessage(null)
      setRecentScans((previous) => {
        if (!import.meta.env.DEV) {
          return previous
        }
        const next = [value, ...previous.filter((item) => item !== value)]
        return next.slice(0, 5)
      })

      const result = await searchProductByEan(value)

      if (result.status === 'found') {
        const product: Product = result.product
        const added = await addProductToSession(product)
        if (added) {
          setStatus(`${product.name} ajouté`)
        }
        return
      }

      if (result.status === 'not-found') {
        setStatus(null)
        setManualEan(value)
        setInputLookupStatus('not-found')
        return
      }

      const err = result.error
      setStatus(null)
      setErrorMessage(
        resolveLifecycleErrorMessage(
          err,
          'Impossible de récupérer le produit. Réessayez ou ajoutez-le manuellement.',
        ),
      )
      setInputLookupStatus('error')
    },
    [addProductToSession, searchProductByEan],
  )

  const trimmedScanValue = scanValue.trim()
  const manualCandidateEan = manualEan.trim() || trimmedScanValue

  useEffect(() => {
    if (!trimmedScanValue) {
      manualLookupIdRef.current += 1
      lastSearchedInputRef.current = null
      setInputLookupStatus('idle')
      return
    }

    if (lastSearchedInputRef.current === trimmedScanValue) {
      return
    }

    lastSearchedInputRef.current = trimmedScanValue
    const currentLookupId = ++manualLookupIdRef.current
    setInputLookupStatus('loading')
    setStatus(`Recherche du code ${trimmedScanValue}`)
    setErrorMessage(null)

    const timeoutId = window.setTimeout(() => {
      void (async () => {
        const result = await searchProductByEan(trimmedScanValue)

        if (manualLookupIdRef.current !== currentLookupId) {
          return
        }

        if (result.status === 'found') {
          const product: Product = result.product
          const added = await addProductToSession(product)
          if (added) {
            setStatus(`${product.name} ajouté`)
            setScanValue('')
            setInputLookupStatus('found')
          } else {
            setInputLookupStatus('error')
          }
        } else if (result.status === 'not-found') {
          setStatus(null)
          setManualEan(trimmedScanValue)
          setErrorMessage('Aucun produit trouvé pour cet EAN. Ajoutez-le manuellement.')
          setInputLookupStatus('not-found')
        } else {
          const err = result.error
          setStatus(null)
          setErrorMessage(
            resolveLifecycleErrorMessage(
              err,
              'Impossible de récupérer le produit. Réessayez ou ajoutez-le manuellement.',
            ),
          )
          setInputLookupStatus('error')
        }
      })()
    }, 300)

    return () => {
      window.clearTimeout(timeoutId)
    }
  }, [addProductToSession, searchProductByEan, trimmedScanValue])

  const handleImagePicked = useCallback(
    async (file: File) => {
      setStatus('Analyse de la photo en cours…')
      setErrorMessage(null)

      let decoded: string | null = null

      try {
        if (typeof window !== 'undefined' && hasBarcodeDetector(window)) {
          try {
            const detector = new window.BarcodeDetector({ formats: ['ean_13', 'ean_8', 'upc_a', 'code_128', 'code_39'] })
            if (typeof window.createImageBitmap === 'function') {
              const bitmap = await window.createImageBitmap(file)
              const results = await detector.detect(bitmap)
              bitmap.close?.()
              decoded = results.find((entry) => entry.rawValue)?.rawValue ?? null
            }
          } catch (error) {
            if (import.meta.env.DEV) {
              console.warn('[scanner] BarcodeDetector indisponible', error)
            }
          }
        }

        if (!decoded) {
          const reader = new BrowserMultiFormatReader(ZXING_IMAGE_HINTS)
          const objectUrl = URL.createObjectURL(file)
          try {
            const image = new Image()
            image.src = objectUrl
            await new Promise<void>((resolve, reject) => {
              image.onload = () => resolve()
              image.onerror = () => reject(new Error('Chargement de la photo impossible.'))
            })
            const result = await reader.decodeFromImageElement(image)
            decoded = result.getText()
          } catch (error) {
            if (import.meta.env.DEV) {
              console.warn('[scanner] Décodage ZXing impossible', error)
            }
          } finally {
            URL.revokeObjectURL(objectUrl)
          }
        }

        if (decoded) {
          await handleDetected(decoded)
        } else {
          setStatus(null)
          setErrorMessage('Impossible de lire ce code-barres sur la photo. Essayez une prise plus nette ou mieux éclairée.')
        }
      } catch (error) {
        setStatus(null)
        if (import.meta.env.DEV) {
          console.error('[scanner] Analyse photo impossible', error)
        }
        setErrorMessage("Échec de l'analyse de la photo. Réessayez avec un autre cliché.")
      }
    },
    [handleDetected],
  )

  const handleInputKeyDown = useCallback(
    (event: KeyboardEvent<HTMLInputElement>) => {
      if (event.key === 'Enter') {
        event.preventDefault()
        const value = trimmedScanValue
        if (value) {
          setScanValue('')
          void handleDetected(value)
        }
      }
    },
    [handleDetected, trimmedScanValue],
  )

  const handleInputChange = useCallback(
    (event: ChangeEvent<HTMLInputElement>) => {
      const { value } = event.target
      if (value.includes('\n') || value.includes('\r')) {
        const [code] = value.split(/\s+/)
        setScanValue('')
        if (code.trim()) {
          void handleDetected(code)
        }
        return
      }
      setScanValue(value)
    },
    [handleDetected],
  )

  const handleManualAdd = useCallback(async () => {
    const sanitizedEan = manualCandidateEan.trim()
    if (!sanitizedEan) {
      setErrorMessage('Indiquez un EAN pour ajouter le produit à la session.')
      return
    }
    if (!/^\d{4,18}$/.test(sanitizedEan)) {
      setErrorMessage("L'EAN doit contenir uniquement des chiffres (4 à 18 caractères).")
      return
    }

    const product: Product = {
      ean: sanitizedEan,
      name: `Produit inconnu EAN ${sanitizedEan}`,
    }

    const added = await addProductToSession(product, { isManual: true })
    if (!added) {
      return
    }

    setStatus(`${product.name} ajouté manuellement`)
    setManualEan('')
    setScanValue('')
    setInputLookupStatus('idle')
    inputRef.current?.focus()
  }, [addProductToSession, manualCandidateEan])

  const canCompleteRun =
    locationId.length > 0 &&
    isValidCountType &&
    ownerUserId.length > 0 &&
    shopId.length > 0 &&
    items.length > 0 &&
    !completionLoading

  const handleCompleteRun = useCallback(async () => {
    if (!isValidCountType) {
      setErrorMessage('Le type de comptage est invalide.')
      return
    }

    if (!shopId) {
      setErrorMessage('Sélectionnez une boutique valide avant de terminer le comptage.')
      return
    }

    if (!ownerUserId) {
      setErrorMessage('Sélectionnez un utilisateur avant de terminer le comptage.')
      return
    }

    if (items.length === 0) {
      setErrorMessage('Ajoutez au moins un article avant de terminer le comptage.')
      return
    }

    if (!locationId) {
      setErrorMessage("Impossible de terminer : la zone sélectionnée n’a pas d’identifiant valide.")
      return
    }
    setCompletionLoading(true)
    setErrorMessage(null)
    setStatus('Envoi du comptage…')
    try {
      const payloadItems = items
        .map((item) => ({
          ean: item.product.ean,
          quantity: item.quantity,
          isManual: Boolean(item.isManual),
        }))
        .filter((entry) => entry.ean.trim().length > 0 && entry.quantity > 0)

      if (payloadItems.length === 0) {
        throw new Error('Ajoutez au moins un article avec une quantité positive pour terminer le comptage.')
      }

      const payload: CompleteInventoryRunPayload = {
        runId: existingRunId || null,
        ownerUserId,
        countType: countType as 1 | 2 | 3,
        items: payloadItems,
      }

      await completeInventoryRun(locationId, payload)
      setStatus('Comptage terminé avec succès.')
      setManualEan('')
      clearSession()
      setScanValue('')
      setInputLookupStatus('idle')
      setRecentScans([])
      const dialog = completionDialogRef.current
      if (dialog && typeof dialog.showModal === 'function') {
        dialog.showModal()
        requestAnimationFrame(() => {
          completionOkButtonRef.current?.focus()
        })
      } else {
        navigate('/', { replace: true })
      }
    } catch (error) {
      setStatus(null)
      const message =
        error instanceof Error && error.message.trim().length > 0
          ? error.message
          : 'Impossible de terminer le comptage.'
      setErrorMessage(message)
    } finally {
      setCompletionLoading(false)
    }
  }, [
    clearSession,
    countType,
    existingRunId,
    isValidCountType,
    items,
    locationId,
    navigate,
    ownerUserId,
    shopId,
  ])

  const handleOpenCompletionConfirmation = useCallback(() => {
    const dialog = completionConfirmationDialogRef.current
    if (dialog && typeof dialog.showModal === 'function') {
      dialog.showModal()
      requestAnimationFrame(() => {
        completionConfirmButtonRef.current?.focus()
      })
      return
    }

    if (window.confirm(
      'Cette action est définitive : une fois validé, ce comptage ne pourra plus être modifié. Confirmez-vous la clôture du comptage ?',
    )) {
      void handleCompleteRun()
    }
  }, [handleCompleteRun])

  const handleCancelCompletionConfirmation = useCallback(() => {
    completionConfirmationDialogRef.current?.close()
  }, [])

  const handleConfirmCompletionConfirmation = useCallback(() => {
    completionConfirmationDialogRef.current?.close()
    void handleCompleteRun()
  }, [handleCompleteRun])

  useEffect(() => {
    const previousCount = previousItemCountRef.current
    previousItemCountRef.current = items.length

    if (!existingRunId || items.length > 0) {
      return
    }

    if (previousCount === 0) {
      return
    }

    if (!locationId || !ownerUserId) {
      return
    }

    let cancelled = false

    void (async () => {
      try {
        await releaseInventoryRun(locationId, existingRunId, ownerUserId)
        if (!cancelled) {
          setSessionId(null)
        }
      } catch (error) {
        if (!cancelled) {
          setErrorMessage(resolveLifecycleErrorMessage(error, 'Impossible de libérer le comptage.'))
        }
      }
    })()

    return () => {
      cancelled = true
    }
  }, [existingRunId, items.length, locationId, ownerUserId, setErrorMessage, setSessionId])

  const handleCompletionModalOk = () => {
    completionDialogRef.current?.close()
    navigate('/', { replace: true })
  }

  const adjustQuantity = (ean: string, delta: number) => {
    const item = items.find((entry) => entry.product.ean === ean)
    if (!item) return
    const nextQuantity = item.quantity + delta
    if (nextQuantity <= 0) {
      removeItem(ean)
    } else {
      setQuantity(ean, nextQuantity)
    }
  }

  return (
    <div className="flex flex-col gap-6" data-testid="page-session">
      <Card className="space-y-4">
        <div className="flex flex-col gap-2">
          <h2 className="text-2xl font-semibold text-slate-900 dark:text-white">Session de comptage</h2>
          <p className="text-sm text-slate-600 dark:text-slate-400">
            {location?.label} • {countType} comptage{countType && countType > 1 ? 's' : ''} •
            {' '}
            {selectedUserDisplayName ?? '–'}
          </p>
          {sessionId && <p className="text-xs text-slate-500 dark:text-slate-400">Session existante #{sessionId}</p>}
        </div>
        <Input
          ref={inputRef}
          name="scanInput"
          label="Scanner (douchette ou saisie)"
          placeholder="Scannez un EAN et validez avec Entrée"
          value={scanValue}
          onChange={handleInputChange}
          onKeyDown={handleInputKeyDown}
          autoFocus
        />
        <div className="flex justify-end">
          <Button
            variant="ghost"
            onClick={() => {
              void handleManualAdd()
            }}
            data-testid="btn-open-manual"
            disabled={!manualCandidateEan || inputLookupStatus !== 'not-found'}
          >
            Ajouter manuellement
          </Button>
        </div>
        {status && <p className="text-sm text-brand-600 dark:text-brand-200">{status}</p>}
        {errorMessage && <p className="text-sm text-red-600 dark:text-red-300">{errorMessage}</p>}
        {import.meta.env.DEV && recentScans.length > 0 && (
          <div className="rounded-2xl border border-slate-300 bg-slate-100 p-3 text-xs text-slate-700 dark:border-slate-600 dark:bg-slate-900 dark:text-slate-300">
            <p className="font-semibold">Derniers scans</p>
            <ul className="mt-1 space-y-1">
              {recentScans.map((value) => (
                <li key={value} className="font-mono">
                  {value}
                </li>
              ))}
            </ul>
          </div>
        )}
      </Card>

      <Card className="space-y-4">
        <div className="flex items-center justify-between">
          <h3 className="text-xl font-semibold text-slate-900 dark:text-white">Articles scannés</h3>
          <span className="text-sm text-slate-600 dark:text-slate-400">{items.length} références</span>
        </div>
        {displayedItems.length === 0 && (
          <EmptyState
            title="En attente de scan"
            description="Scannez un produit via la caméra ou la douchette pour l&apos;ajouter au comptage."
          />
        )}
        <ul className="flex flex-col gap-3">
          {displayedItems.map((item) => (
            <li
              key={item.id}
              className="flex items-center justify-between rounded-2xl border border-slate-200 bg-white p-4 dark:border-slate-700 dark:bg-slate-900/60"
              data-testid="scanned-item"
              data-item-id={item.id}
              data-ean={item.product.ean}
            >
              <div>
                <p className="text-lg font-semibold text-slate-900 dark:text-white">{item.product.name}</p>
                <p className="text-xs text-slate-500 dark:text-slate-400">EAN {item.product.ean}</p>
                {item.isManual && <p className="text-xs text-amber-600 dark:text-amber-300">Ajout manuel</p>}
              </div>
              <div className="flex items-center gap-3">
                <Button
                  type="button"
                  variant="secondary"
                  className="tap-highlight-none no-focus-ring btn-glyph-center h-10 w-10 text-xl font-semibold"
                  onClick={() => adjustQuantity(item.product.ean, -1)}
                  aria-label="Retirer"
                >
                  −
                </Button>
                <span className="text-2xl font-bold text-slate-900 dark:text-white">{item.quantity}</span>
                <Button
                  type="button"
                  className="tap-highlight-none no-focus-ring btn-glyph-center h-10 w-10 text-xl font-semibold"
                  onClick={() => adjustQuantity(item.product.ean, 1)}
                  aria-label="Ajouter"
                >
                  +
                </Button>
              </div>
            </li>
          ))}
        </ul>
        {displayedItems.length > 0 && (
          <div className="flex justify-end">
            <Button
              data-testid="btn-complete-run"
              className="py-3"
              disabled={!canCompleteRun}
              aria-disabled={!canCompleteRun}
              onClick={handleOpenCompletionConfirmation}
            >
              {completionLoading ? 'Enregistrement…' : 'Terminer ce comptage'}
            </Button>
          </div>
        )}
      </Card>
      <dialog
        ref={completionConfirmationDialogRef}
        aria-modal="true"
        className="max-w-lg rounded-2xl border border-slate-300 bg-white p-6 text-slate-900 shadow-xl backdrop:bg-black/40 dark:border-slate-700 dark:bg-slate-900 dark:text-white"
      >
        <div className="space-y-4">
          <p className="text-lg font-semibold">Confirmer la clôture du comptage</p>
          <p className="text-sm text-slate-600 dark:text-slate-300">
            Cette action est définitive&nbsp;: une fois validé, ce comptage ne pourra plus être modifié. Êtes-vous certain de vouloir terminer ce comptage&nbsp;?
          </p>
        </div>
        <div className="mt-6 flex justify-end gap-3">
          <Button type="button" variant="secondary" onClick={handleCancelCompletionConfirmation}>
            Annuler
          </Button>
          <Button
            ref={completionConfirmButtonRef}
            type="button"
            onClick={handleConfirmCompletionConfirmation}
            data-testid="btn-confirm-complete"
          >
            Confirmer la clôture
          </Button>
        </div>
      </dialog>
      <dialog
        ref={completionDialogRef}
        id="complete-inventory-modal"
        aria-modal="true"
        className="rounded-2xl border border-slate-300 bg-white p-6 text-slate-900 shadow-xl backdrop:bg-black/40 dark:border-slate-700 dark:bg-slate-900 dark:text-white"
      >
        <p className="text-lg font-semibold">Le comptage a été enregistré avec succès.</p>
        <div className="mt-6 flex justify-end">
          <Button
            ref={completionOkButtonRef}
            type="button"
            onClick={handleCompletionModalOk}
            data-testid="btn-complete-ok"
          >
            OK
          </Button>
        </div>
      </dialog>
      <Card className="space-y-4">
        <div className="flex flex-col gap-2">
          <h3 className="text-xl font-semibold text-slate-900 dark:text-white">Scanner avec le téléphone</h3>
          <p className="text-sm text-slate-600 dark:text-slate-400">
            Utilisez l’appareil photo pour lire un code-barres lorsque la douchette n’est pas disponible.
          </p>
        </div>
        <Button variant="secondary" onClick={() => setUseCamera((prev) => !prev)}>
          {useCamera ? 'Désactiver la caméra' : 'Activer la caméra'}
        </Button>
        <BarcodeScanner
          active={useCamera}
          onDetected={handleDetected}
          onError={(message) => {
            setStatus(null)
            setErrorMessage(message)
          }}
          onPickImage={(file) => void handleImagePicked(file)}
          preferredFormats={['EAN_13', 'EAN_8', 'CODE_128', 'CODE_39', 'ITF', 'QR_CODE']}
        />
      </Card>

    </div>
  )
}
