// Modifications : forcer l'inclusion de runId=null lors de la complétion sans run existant.
import type { KeyboardEvent, ChangeEvent, FocusEvent, PointerEvent } from 'react'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate, useOutletContext } from 'react-router-dom'
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
import { ConflictZoneModal } from '../../components/Conflicts/ConflictZoneModal'
import { MobileActionBar } from '../../components/MobileActionBar'
import { useInventory } from '../../contexts/InventoryContext'
import { useScanFeedback } from '../../hooks/useScanFeedback'
import type { HttpError } from '@/lib/api/http'
import type { ConflictZoneSummary, Product } from '../../types/inventory'
import { CountType } from '../../types/inventory'
import { useShop } from '@/state/ShopContext'
import type { InventoryLayoutOutletContext } from './InventoryLayout'

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

const MIN_EAN_LENGTH = 8
const MAX_EAN_LENGTH = 13

const sanitizeEan = (value: string) => value.replace(/\D+/g, '')

const isEanLengthValid = (ean: string) => ean.length >= MIN_EAN_LENGTH && ean.length <= MAX_EAN_LENGTH

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
    logs,
    logEvent,
  } = useInventory()
  const { shop } = useShop()
  const { playSuccess, playError } = useScanFeedback()
  const outletContext = useOutletContext<InventoryLayoutOutletContext | null | undefined>()
  const setMobileNav = outletContext?.setMobileNav
  const [useCamera, setUseCamera] = useState(false)
  const [status, setStatusState] = useState<string | null>(null)
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
  const logsDialogRef = useRef<HTMLDialogElement | null>(null)
  const [recentScans, setRecentScans] = useState<string[]>([])
  const manualLookupIdRef = useRef(0)
  const lastSearchedInputRef = useRef<string | null>(null)
  const previousItemCountRef = useRef(items.length)
  const [quantityDrafts, setQuantityDrafts] = useState<Record<string, string>>({})
  const [conflictModalOpen, setConflictModalOpen] = useState(false)

  const updateStatus = useCallback(
    (message: string | null) => {
      setStatusState(message)
      if (message) {
        const normalized = message.trim().toLowerCase()
        const isLookupMessage = normalized.startsWith('recherche du code')
        const containsProductAddition =
          normalized.includes('ajouté') || normalized.includes('ajoutée')
        if (!isLookupMessage && !containsProductAddition) {
          logEvent({
            type: 'status',
            message,
          })
        }
      }
    },
    [logEvent],
  )

  const handleOpenLogsDialog = useCallback(() => {
    const dialog = logsDialogRef.current
    if (!dialog) {
      return
    }

    if (typeof dialog.showModal === 'function') {
      dialog.showModal()
      return
    }

    dialog.setAttribute('open', '')
  }, [])

  const handleCloseLogsDialog = useCallback(() => {
    const dialog = logsDialogRef.current
    if (!dialog) {
      return
    }

    if (typeof dialog.close === 'function') {
      dialog.close()
      return
    }

    dialog.removeAttribute('open')
  }, [])

  const formatLogTimestamp = useCallback((value: string) => {
    const date = new Date(value)
    if (Number.isNaN(date.getTime())) {
      return value
    }
    return new Intl.DateTimeFormat('fr-FR', {
      dateStyle: 'short',
      timeStyle: 'medium',
    }).format(date)
  }, [])

  const selectedUserDisplayName = selectedUser?.displayName ?? null
  const countTypeLabel = typeof countType === 'number' ? `Comptage n°${countType}` : 'Comptage n°–'
  const ownerUserId = selectedUser?.id?.trim() ?? ''
  const existingRunId = typeof sessionId === 'string' ? sessionId.trim() : ''
  const locationId = location?.id?.trim() ?? ''
  const shopId = shop?.id?.trim() ?? ''
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
    if (typeof countType !== 'number' || countType < CountType.Count3 || !location) {
      setConflictModalOpen(false)
    }
  }, [countType, location])

  useEffect(() => {
    if (!selectedUser) {
      navigate('/select-shop', { replace: true })
    } else if (!locationId) {
      navigate('/inventory/location', { replace: true })
    } else if (!countType) {
      navigate('/inventory/count-type', { replace: true })
    }
  }, [countType, locationId, navigate, selectedUser])

  const displayedItems = items

  const isValidCountType = typeof countType === 'number' && countType >= 1

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
      countType: countType as number,
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
          updateStatus(null)
          setErrorMessage(message)
          return false
        }
      }

      setErrorMessage(null)
      addOrIncrementItem(product, options)
      return true
    },
    [addOrIncrementItem, ensureActiveRun, items.length, setErrorMessage, updateStatus],
  )

  const handleDetected = useCallback(
    async (rawValue: string) => {
      const value = sanitizeEan(rawValue.trim())
      if (!value) {
        return
      }

      if (!isEanLengthValid(value)) {
        updateStatus(null)
        setErrorMessage(
          `EAN ${value} invalide : saisissez un code entre ${MIN_EAN_LENGTH} et ${MAX_EAN_LENGTH} chiffres.`,
        )
        setManualEan('')
        setInputLookupStatus('idle')
        playError()
        return
      }

      updateStatus(`Recherche du code ${value}`)
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
          updateStatus(`${product.name} ajouté`)
        }
        playSuccess()
        return
      }

      if (result.status === 'not-found') {
        updateStatus(null)
        setManualEan(value)
        setInputLookupStatus('not-found')
        playError()
        return
      }

      const err = result.error
      updateStatus(null)
      setErrorMessage(
        resolveLifecycleErrorMessage(
          err,
          'Impossible de récupérer le produit. Réessayez ou ajoutez-le manuellement.',
        ),
      )
      setInputLookupStatus('error')
      playError()
    },
    [addProductToSession, playError, playSuccess, searchProductByEan, updateStatus],
  )

  const trimmedScanValue = sanitizeEan(scanValue.trim())
  const scanInputError = useMemo(() => {
    if (!trimmedScanValue) {
      return null
    }
    if (trimmedScanValue.length < MIN_EAN_LENGTH) {
      const remaining = MIN_EAN_LENGTH - trimmedScanValue.length
      return `Ajoutez encore ${remaining} chiffre${remaining > 1 ? 's' : ''} pour valider l’EAN.`
    }
    if (trimmedScanValue.length > MAX_EAN_LENGTH) {
      return `EAN trop long : ${MAX_EAN_LENGTH} chiffres maximum.`
    }
    return null
  }, [trimmedScanValue])

  const manualCandidateEan = (manualEan ? sanitizeEan(manualEan) : trimmedScanValue).trim()
  const isManualCandidateValid = manualCandidateEan.length > 0 && isEanLengthValid(manualCandidateEan)

  useEffect(() => {
    if (!trimmedScanValue) {
      manualLookupIdRef.current += 1
      lastSearchedInputRef.current = null
      setInputLookupStatus('idle')
      return
    }

    if (scanInputError) {
      manualLookupIdRef.current += 1
      lastSearchedInputRef.current = null
      setInputLookupStatus('idle')
      setErrorMessage(null)
      return
    }

    if (lastSearchedInputRef.current === trimmedScanValue) {
      return
    }

    lastSearchedInputRef.current = trimmedScanValue
    const currentLookupId = ++manualLookupIdRef.current
    setInputLookupStatus('loading')
    updateStatus(`Recherche du code ${trimmedScanValue}`)
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
            updateStatus(`${product.name} ajouté`)
            setScanValue('')
            setInputLookupStatus('found')
          } else {
            setInputLookupStatus('error')
          }
        } else if (result.status === 'not-found') {
          updateStatus(null)
          setManualEan(trimmedScanValue)
          setErrorMessage('Aucun produit trouvé pour cet EAN. Ajoutez-le manuellement.')
          setInputLookupStatus('not-found')
        } else {
          const err = result.error
          updateStatus(null)
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
  }, [addProductToSession, scanInputError, searchProductByEan, trimmedScanValue, updateStatus])

  const handleImagePicked = useCallback(
    async (file: File) => {
      updateStatus('Analyse de la photo en cours…')
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
          updateStatus(null)
          setErrorMessage('Impossible de lire ce code-barres sur la photo. Essayez une prise plus nette ou mieux éclairée.')
        }
      } catch (error) {
        updateStatus(null)
        if (import.meta.env.DEV) {
          console.error('[scanner] Analyse photo impossible', error)
        }
        setErrorMessage("Échec de l'analyse de la photo. Réessayez avec un autre cliché.")
      }
    },
    [handleDetected, updateStatus],
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
        setManualEan('')
        if (code.trim()) {
          void handleDetected(code)
        }
        return
      }
      const sanitized = sanitizeEan(value)
      setScanValue(sanitized)
      setManualEan('')
      setErrorMessage(null)
    },
    [handleDetected],
  )

  const handleManualAdd = useCallback(async () => {
    const sanitizedEan = sanitizeEan(manualCandidateEan)
    if (!sanitizedEan) {
      setErrorMessage('Indiquez un EAN pour ajouter le produit à la session.')
      return
    }
    if (!isEanLengthValid(sanitizedEan)) {
      setErrorMessage(
        `Un EAN doit contenir entre ${MIN_EAN_LENGTH} et ${MAX_EAN_LENGTH} chiffres (code saisi : ${sanitizedEan.length}).`,
      )
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

    updateStatus(`${product.name} ajouté manuellement`)
    setManualEan('')
    setScanValue('')
    setInputLookupStatus('idle')
    inputRef.current?.focus()
  }, [addProductToSession, manualCandidateEan, updateStatus])

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
    updateStatus('Envoi du comptage…')
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
        countType: countType as number,
        items: payloadItems,
      }

      await completeInventoryRun(locationId, payload)
      updateStatus('Comptage terminé avec succès.')
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
      updateStatus(null)
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
    updateStatus,
  ])

  const handleRestartSession = useCallback(() => {
    const shouldReset =
      items.length === 0 ||
      window.confirm('Relancer un comptage ? Les articles non enregistrés seront perdus.')

    if (!shouldReset) {
      return
    }

    completionConfirmationDialogRef.current?.close()
    logsDialogRef.current?.close()
    clearSession()
    setSessionId(null)
    setManualEan('')
    setScanValue('')
    setInputLookupStatus('idle')
    setQuantityDrafts({})
    setRecentScans([])
    setStatusState(null)
    setErrorMessage(null)
    setUseCamera(false)
    updateStatus('Session réinitialisée.')
  }, [
    clearSession,
    items.length,
    setInputLookupStatus,
    setManualEan,
    setQuantityDrafts,
    setRecentScans,
    setScanValue,
    setSessionId,
    setStatusState,
    setErrorMessage,
    setUseCamera,
    updateStatus,
  ])

  const toggleCamera = useCallback(() => {
    setUseCamera((prev) => !prev)
  }, [setUseCamera])

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

  const mobileActions = useMemo(
    () => (
      <MobileActionBar
        scan={{
          label: useCamera ? 'Caméra activée' : 'Scanner',
          onClick: toggleCamera,
        }}
        restart={{
          onClick: handleRestartSession,
          disabled: completionLoading,
        }}
        complete={{
          onClick: handleOpenCompletionConfirmation,
          disabled: !canCompleteRun,
          busy: completionLoading,
          label: completionLoading ? 'Enregistrement…' : 'Terminer',
        }}
      />
    ),
    [
      canCompleteRun,
      completionLoading,
      handleOpenCompletionConfirmation,
      handleRestartSession,
      toggleCamera,
      useCamera,
    ],
  )

  useEffect(() => {
    if (!setMobileNav) {
      return undefined
    }
    setMobileNav(mobileActions)
    return () => setMobileNav(null)
  }, [mobileActions, setMobileNav])

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

  const clearQuantityDraft = useCallback((ean: string) => {
    setQuantityDrafts((prev) => {
      if (!(ean in prev)) {
        return prev
      }
      const { [ean]: _discarded, ...rest } = prev
      return rest
    })
  }, [])

  const adjustQuantity = (ean: string, delta: number) => {
    const item = items.find((entry) => entry.product.ean === ean)
    if (!item) return
    const nextQuantity = item.quantity + delta
    if (nextQuantity <= 0) {
      clearQuantityDraft(ean)
      removeItem(ean)
    } else {
      setQuantity(ean, nextQuantity)
      clearQuantityDraft(ean)
    }
  }

  const commitQuantity = useCallback(
    (ean: string, rawValue: string | undefined) => {
      const sanitized = (rawValue ?? '').replace(/\D+/g, '').slice(0, 4)

      if (sanitized.length === 0) {
        clearQuantityDraft(ean)
        return
      }

      const parsed = Number.parseInt(sanitized, 10)
      if (!Number.isFinite(parsed) || parsed <= 0) {
        clearQuantityDraft(ean)
        removeItem(ean)
        return
      }

      setQuantity(ean, parsed)
      clearQuantityDraft(ean)
    },
    [clearQuantityDraft, removeItem, setQuantity],
  )

  const handleQuantityInputChange = useCallback(
    (ean: string, event: ChangeEvent<HTMLInputElement>) => {
      const sanitized = event.target.value.replace(/\D+/g, '').slice(0, 4)

      setQuantityDrafts((prev) => {
        const nextValue = sanitized
        const previousValue = prev[ean] ?? ''
        if (nextValue === previousValue) {
          return prev
        }
        return { ...prev, [ean]: nextValue }
      })
    },
    [],
  )

  const handleQuantityBlur = useCallback(
    (ean: string, event: FocusEvent<HTMLInputElement>) => {
      commitQuantity(ean, event.currentTarget.value)
    },
    [commitQuantity],
  )

  const handleQuantityKeyDown = useCallback(
    (ean: string, event: KeyboardEvent<HTMLInputElement>) => {
      if (event.key === 'Enter') {
        event.preventDefault()
        commitQuantity(ean, event.currentTarget.value)
      }

      if (event.key === 'Escape') {
        event.preventDefault()
        clearQuantityDraft(ean)
        event.currentTarget.blur()
      }
    },
    [clearQuantityDraft, commitQuantity],
  )

  const handleQuantityFocus = useCallback((event: FocusEvent<HTMLInputElement>) => {
    const input = event.currentTarget
    requestAnimationFrame(() => {
      input.select()
    })
  }, [])

  const handleQuantityPointerDown = useCallback((event: PointerEvent<HTMLInputElement>) => {
    const input = event.currentTarget
    if (document.activeElement !== input) {
      event.preventDefault()
      input.focus()
    }
  }, [])

  useEffect(() => {
    setQuantityDrafts((prev) => {
      const validEans = new Set(items.map((entry) => entry.product.ean))
      let changed = false
      const next: Record<string, string> = {}

      for (const [ean, value] of Object.entries(prev)) {
        if (validEans.has(ean)) {
          next[ean] = value
        } else {
          changed = true
        }
      }

      if (!changed && Object.keys(next).length === Object.keys(prev).length) {
        return prev
      }

      return next
    })
  }, [items])

  return (
    <div className="flex flex-col gap-6" data-testid="page-session">
      <Card className="space-y-4">
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div className="flex flex-col gap-2">
            <h2 className="text-2xl font-semibold text-slate-900 dark:text-white">Session de comptage</h2>
            <p className="text-sm text-slate-600 dark:text-slate-400">
              {location?.label} • {countTypeLabel} •
              {' '}
              {selectedUserDisplayName ?? '–'}
            </p>
          </div>
          <Button
            type="button"
            variant="secondary"
            className="self-start"
            onClick={handleOpenLogsDialog}
            aria-haspopup="dialog"
            data-testid="btn-open-logs"
          >
            Journal des actions ({logs.length})
          </Button>
        </div>
        <div className="space-y-1">
          <Input
            ref={inputRef}
            name="scanInput"
            label="Scanner (douchette ou saisie)"
            placeholder="Scannez un EAN et validez avec Entrée"
            value={scanValue}
            onChange={handleInputChange}
            onKeyDown={handleInputKeyDown}
            inputMode="numeric"
            pattern="\d*"
            maxLength={MAX_EAN_LENGTH}
            autoComplete="off"
            aria-invalid={Boolean(scanInputError)}
            autoFocus
          />
          <p
            className={`text-xs ${
              scanInputError ? 'text-rose-600 dark:text-rose-300' : 'text-slate-500 dark:text-slate-400'
            }`}
            aria-live="polite"
          >
            {scanInputError ?? `EAN attendu : ${MIN_EAN_LENGTH} à ${MAX_EAN_LENGTH} chiffres.`}
          </p>
        </div>
        <div className="flex justify-end">
          <Button
            variant="ghost"
            onClick={() => {
              void handleManualAdd()
            }}
            data-testid="btn-open-manual"
            disabled={!manualCandidateEan || inputLookupStatus !== 'not-found' || !isManualCandidateValid}
          >
            Ajouter manuellement
          </Button>
        </div>
        {status && (
          <p className="text-sm text-brand-600 dark:text-brand-200" data-testid="status-message">
            {status}
          </p>
        )}
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

      <dialog
        ref={logsDialogRef}
        aria-modal="true"
        aria-labelledby="session-log-title"
        className="max-w-2xl rounded-2xl border border-slate-300 bg-white p-6 text-slate-900 shadow-xl backdrop:bg-black/40 dark:border-slate-700 dark:bg-slate-900 dark:text-white"
      >
        <div className="flex items-start justify-between gap-4">
          <div>
            <p id="session-log-title" className="text-lg font-semibold">
              Journal de session
            </p>
            <p className="text-sm text-slate-600 dark:text-slate-300">
              Historique des scans, ajouts et ajustements réalisés pendant ce comptage.
            </p>
          </div>
          <Button type="button" variant="ghost" onClick={handleCloseLogsDialog}>
            Fermer
          </Button>
        </div>
        <div className="mt-4 max-h-96 overflow-y-auto">
          {logs.length === 0 ? (
            <p className="text-sm text-slate-600 dark:text-slate-400">Aucun évènement enregistré pour l’instant.</p>
          ) : (
            <ul className="space-y-3" data-testid="logs-list">
              {logs.map((entry) => (
                <li
                  key={entry.id}
                  className="rounded-xl border border-slate-200 bg-white p-3 text-sm dark:border-slate-600 dark:bg-slate-900/60"
                >
                  <p className="font-semibold text-slate-900 dark:text-white">{entry.message}</p>
                  <p className="text-xs text-slate-500 dark:text-slate-400">{formatLogTimestamp(entry.timestamp)}</p>
                  {entry.context?.ean && (
                    <p className="text-xs text-slate-500 dark:text-slate-400">
                      EAN {entry.context.ean}
                      {typeof entry.context.quantity === 'number' ? ` • Quantité ${entry.context.quantity}` : ''}
                    </p>
                  )}
                </li>
              ))}
            </ul>
          )}
        </div>
        <p className="mt-6 text-xs text-slate-500 dark:text-slate-400">Le journal est réinitialisé quand le comptage est terminé.</p>
      </dialog>

      <Card className="space-y-4">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div className="flex flex-col gap-1">
            <h3 className="text-xl font-semibold text-slate-900 dark:text-white">Articles scannés</h3>
            <span className="text-sm text-slate-600 dark:text-slate-400">{items.length} références</span>
          </div>
          {conflictZoneSummary && (
            <div className="flex flex-wrap items-center gap-3 sm:justify-end">
              <span className="inline-flex items-center gap-2 rounded-full border border-rose-200 bg-rose-50 px-3 py-1 text-xs font-semibold uppercase tracking-wide text-rose-700 dark:border-rose-500/50 dark:bg-rose-500/10 dark:text-rose-200">
                <span aria-hidden>⚠️</span>
                Zone en conflit
              </span>
              <Button
                variant="ghost"
                className="inline-flex items-center gap-2 rounded-2xl border border-rose-200/80 bg-white/70 px-3 py-2 text-sm font-semibold text-rose-700 transition hover:bg-rose-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-rose-400 focus-visible:ring-offset-2 dark:border-rose-500/40 dark:bg-rose-500/10 dark:text-rose-200 dark:hover:bg-rose-500/20"
                onClick={() => setConflictModalOpen(true)}
                data-testid="btn-view-conflicts"
              >
                Voir les écarts C1/C2
              </Button>
            </div>
          )}
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
                <input
                  type="text"
                  inputMode="numeric"
                  pattern="[0-9]*"
                  maxLength={4}
                  data-testid="quantity-input"
                  aria-label={`Quantité pour ${item.product.name}`}
                  value={quantityDrafts[item.product.ean] ?? String(item.quantity)}
                  onChange={(event) => handleQuantityInputChange(item.product.ean, event)}
                  onBlur={(event) => handleQuantityBlur(item.product.ean, event)}
                  onKeyDown={(event) => handleQuantityKeyDown(item.product.ean, event)}
                  onFocus={handleQuantityFocus}
                  onPointerDown={handleQuantityPointerDown}
                  className="h-12 w-20 rounded-xl border border-slate-300 bg-white text-center text-2xl font-bold text-slate-900 outline-none focus-visible:border-brand-400 focus-visible:ring-2 focus-visible:ring-brand-200 dark:border-slate-600 dark:bg-slate-800 dark:text-white"
                  autoComplete="off"
                />
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
            updateStatus(null)
            setErrorMessage(message)
          }}
          onPickImage={(file) => void handleImagePicked(file)}
          preferredFormats={['EAN_13', 'EAN_8', 'CODE_128', 'CODE_39', 'ITF', 'QR_CODE']}
        />
      </Card>

      <ConflictZoneModal
        open={Boolean(conflictZoneSummary) && conflictModalOpen}
        zone={conflictZoneSummary}
        onClose={() => setConflictModalOpen(false)}
      />

    </div>
  )
}
