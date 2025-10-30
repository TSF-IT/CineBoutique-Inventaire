import type { ChangeEvent, FocusEvent, PointerEvent, KeyboardEvent } from 'react'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'

import {
  completeInventoryRun,
  fetchProductByEan,
  getConflictZoneDetail,
  releaseInventoryRun,
  startInventoryRun,
  type CompleteInventoryRunItem,
  type CompleteInventoryRunPayload,
} from '../../api/inventoryApi'
import { Card } from '../../components/Card'
import { ConflictZoneModal } from '../../components/Conflicts/ConflictZoneModal'
import { EmptyState } from '../../components/EmptyState'
import { LoadingIndicator } from '../../components/LoadingIndicator'
import { Button } from '../../components/ui/Button'
import { Input } from '../../components/ui/Input'
import { useInventory } from '../../contexts/InventoryContext'
import type { ConflictZoneItem, ConflictZoneSummary, InventoryItem, Product } from '../../types/inventory'
import { CountType } from '../../types/inventory'

import { ProductsModal, type ProductsModalItem } from '@/components/products/ProductsModal'
import { useScanRejectionFeedback } from '@/hooks/useScanRejectionFeedback'
import type { HttpError } from '@/lib/api/http'
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

const MAX_SCAN_LENGTH = 32

const sanitizeScanValue = (value: string) => value.normalize('NFKC').replace(/\r|\n/g, '')

const normalizeIdentifier = (value: string | null | undefined) => {
  const trimmed = value?.trim()
  return trimmed && trimmed.length > 0 ? trimmed : null
}

const formatIdentifierForDisplay = (value: string | null | undefined): string =>
  normalizeIdentifier(value) ?? '—'

const IDENTIFIER_MIN_LENGTH = 3

const sanitizeEanForSubmission = (value: string | null | undefined): string | null => {
  const normalized = normalizeIdentifier(value)?.normalize('NFKC')
  if (!normalized) {
    return null
  }

  const collapsed = normalized.replace(/[\s.-]+/g, '')
  return collapsed.length > 0 ? collapsed.toUpperCase() : null
}

const isValidEanForSubmission = (ean: string | null | undefined): ean is string => {
  if (!ean) {
    return false
  }

  if (ean.length < IDENTIFIER_MIN_LENGTH || ean.length > MAX_SCAN_LENGTH) {
    return false
  }

  return /^[\p{L}\p{N}'#°._-]+$/u.test(ean)
}

const dedupeConflictItems = (items: ConflictZoneItem[]): ConflictZoneItem[] => {
  const seen = new Set<string>()
  const result: ConflictZoneItem[] = []

  items.forEach((item, index) => {
    const ean = normalizeIdentifier(item.ean)
    const productId = normalizeIdentifier(item.productId)
    const sku = normalizeIdentifier(item.sku)

    const key = ean ?? productId ?? sku ?? `index:${index}`
    if (seen.has(key)) {
      return
    }

    seen.add(key)
    result.push(item)
  })

  return result
}

// eslint-disable-next-line react-refresh/only-export-components
export const aggregateItemsForCompletion = (
  items: InventoryItem[],
): CompleteInventoryRunItem[] => {
  const aggregated = new Map<string, CompleteInventoryRunItem>()

  for (const item of items) {
    const normalizedEan = sanitizeEanForSubmission(item.product.ean)
    const quantity = Number.isFinite(item.quantity) ? item.quantity : 0

    if (!normalizedEan || !isValidEanForSubmission(normalizedEan) || quantity <= 0) {
      continue
    }

    const existing = aggregated.get(normalizedEan)
    if (existing) {
      existing.quantity += quantity
      existing.isManual = existing.isManual || Boolean(item.isManual)
      continue
    }

    aggregated.set(normalizedEan, {
      ean: normalizedEan,
      quantity,
      isManual: Boolean(item.isManual),
    })
  }

  return Array.from(aggregated.values())
}

const collectInvalidEanLabels = (items: InventoryItem[]): string[] => {
  const labels: string[] = []

  for (const item of items) {
    const quantity = Number.isFinite(item.quantity) ? item.quantity : 0
    if (quantity <= 0) {
      continue
    }

    const sanitized = sanitizeEanForSubmission(item.product.ean)
    if (isValidEanForSubmission(sanitized)) {
      continue
    }

    const normalizedDisplay = normalizeIdentifier(item.product.ean)
    if (normalizedDisplay) {
      labels.push(normalizedDisplay)
      continue
    }

    labels.push(`${item.product.name} (EAN manquant)`)
  }

  return labels
}

const formatInvalidEanSummary = (labels: string[]): string => {
  if (labels.length === 0) {
    return ''
  }

  const preview = labels.slice(0, 3).map((label) => `«${label}»`).join(', ')
  const remaining = labels.length - 3

  if (remaining <= 0) {
    return preview
  }

  const plural = remaining > 1 ? 's' : ''
  return `${preview}… (+${remaining} autre${plural})`
}

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
    initializeItems,
    setQuantity,
    removeItem,
    sessionId,
    setSessionId,
    clearSession,
    logs,
    logEvent,
  } = useInventory()
  const { shop } = useShop()
  const triggerScanRejectionFeedback = useScanRejectionFeedback()
  const [status, setStatusState] = useState<string | null>(null)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [scanValue, setScanValue] = useState('')
  const [, setInputLookupStatus] = useState<'idle' | 'loading' | 'found' | 'not-found' | 'error'>('idle')
  const [completionLoading, setCompletionLoading] = useState(false)
  const completionDialogRef = useRef<HTMLDialogElement | null>(null)
  const completionConfirmationDialogRef = useRef<HTMLDialogElement | null>(null)
  const completionOkButtonRef = useRef<HTMLButtonElement | null>(null)
  const completionConfirmButtonRef = useRef<HTMLButtonElement | null>(null)
  const inputRef = useRef<HTMLInputElement | null>(null)
  const logsDialogRef = useRef<HTMLDialogElement | null>(null)
  const manualLookupIdRef = useRef(0)
  const lastSearchedInputRef = useRef<string | null>(null)
  const previousItemCountRef = useRef(items.length)
  const pendingFocusEanRef = useRef<string | null>(null)
  const [quantityDrafts, setQuantityDrafts] = useState<Record<string, string>>({})
  const [conflictModalOpen, setConflictModalOpen] = useState(false)
  const [conflictPrefillStatus, setConflictPrefillStatus] = useState<'idle' | 'loading' | 'loaded' | 'error'>('idle')
  const [conflictPrefillError, setConflictPrefillError] = useState<string | null>(null)
  const [conflictPrefillAttempt, setConflictPrefillAttempt] = useState(0)
  const conflictPrefillKeyRef = useRef<string | null>(null)
  const [catalogueOpen, setCatalogueOpen] = useState(false)

  const isConflictResolutionMode = typeof countType === 'number' && countType >= CountType.Count3

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
    if (!isConflictResolutionMode || !location) {
      return null
    }

    return {
      locationId: location.id,
      locationCode: location.code,
      locationLabel: location.label,
      conflictLines: 0,
    }
  }, [isConflictResolutionMode, location])

  useEffect(() => {
    if (!isConflictResolutionMode || !location) {
      setConflictModalOpen(false)
    }
  }, [isConflictResolutionMode, location])

  useEffect(() => {
    if (!isConflictResolutionMode) {
      setConflictPrefillStatus('idle')
      setConflictPrefillError(null)
      conflictPrefillKeyRef.current = null
    }
  }, [isConflictResolutionMode])

  useEffect(() => {
    if (!isConflictResolutionMode) {
      return
    }

    if (!locationId) {
      setConflictPrefillStatus('idle')
      setConflictPrefillError('Impossible de charger les références en conflit.')
      return
    }

    const key = `${locationId}:${countType ?? ''}`

    if (items.length > 0 && conflictPrefillKeyRef.current === key) {
      setConflictPrefillStatus('loaded')
      setConflictPrefillError(null)
      return
    }

    if (items.length > 0 && conflictPrefillKeyRef.current !== key) {
      conflictPrefillKeyRef.current = key
      setConflictPrefillStatus('loaded')
      setConflictPrefillError(null)
      return
    }

    let cancelled = false
    const abortController = new AbortController()

    const buildFallbackProduct = (ean: string, sku?: string | null): Product => {
      const normalizedEan = (ean ?? '').trim()
      const normalizedSku = typeof sku === 'string' ? sku.trim() : ''
      const label = normalizedSku
        ? `SKU ${normalizedSku}`
        : normalizedEan
          ? `EAN ${normalizedEan}`
          : 'Produit en conflit'
      return {
        ean: normalizedEan || ean,
        name: label,
        sku: normalizedSku || undefined,
      }
    }

    const loadConflicts = async () => {
      try {
        const detail = await getConflictZoneDetail(locationId, abortController.signal)
        if (cancelled) {
          return
        }

        const rawConflictItems = Array.isArray(detail.items) ? detail.items : []
        const conflictItems = dedupeConflictItems(rawConflictItems)

        if (conflictItems.length === 0) {
          initializeItems([])
          setConflictPrefillStatus('loaded')
          conflictPrefillKeyRef.current = key
          return
        }

        const productCache = new Map<string, Promise<Product>>()
        const resolveProduct = (ean: string, sku?: string | null) => {
          const normalizedEan = (ean ?? '').trim()
          if (!normalizedEan) {
            return Promise.resolve(buildFallbackProduct(ean, sku))
          }
          const cached = productCache.get(normalizedEan)
          if (cached) {
            return cached
          }
          const request = (async () => {
            try {
              return await fetchProductByEan(normalizedEan)
            } catch (error) {
              console.warn('[inventory] produit conflit introuvable, fallback', normalizedEan, error)
              return buildFallbackProduct(normalizedEan, sku)
            }
          })()
          productCache.set(normalizedEan, request)
          return request
        }

        const products = await Promise.all(
          conflictItems.map((item) => resolveProduct(item.ean ?? '', item.sku ?? undefined)),
        )

        if (cancelled) {
          return
        }

        initializeItems(
          conflictItems.map((item, index) => {
            const resolved = products[index]
            const normalizedEan = (item.ean ?? '').trim()
            return {
              product: {
                ...resolved,
                ean: resolved.ean?.trim().length ? resolved.ean : normalizedEan || resolved.ean,
                sku:
                  resolved.sku && resolved.sku.trim().length > 0
                    ? resolved.sku
                    : item.sku?.trim().length
                      ? item.sku
                      : undefined,
              },
              quantity: 0,
              hasConflict: true,
            }
          }),
        )

        setConflictPrefillStatus('loaded')
        setConflictPrefillError(null)
        conflictPrefillKeyRef.current = key
      } catch (error) {
        if (cancelled) {
          return
        }
        if (error instanceof DOMException && error.name === 'AbortError') {
          return
        }
        console.error('[inventory] échec chargement références conflit', error)
        setConflictPrefillError('Impossible de charger les références en conflit.')
        setConflictPrefillStatus('error')
        conflictPrefillKeyRef.current = null
      }
    }

    setConflictPrefillStatus('loading')
    setConflictPrefillError(null)
    conflictPrefillKeyRef.current = key
    void loadConflicts()

    return () => {
      cancelled = true
      abortController.abort()
    }
  }, [
    countType,
    initializeItems,
    isConflictResolutionMode,
    items.length,
    locationId,
    conflictPrefillAttempt,
  ])

  useEffect(() => {
    if (!selectedUser) {
      navigate('/select-shop', { replace: true })
    } else if (!locationId) {
      navigate('/inventory/location', { replace: true })
    } else if (!countType) {
      navigate('/inventory/count-type', { replace: true })
    }
  }, [countType, locationId, navigate, selectedUser])

  const handleRetryConflictPrefill = useCallback(() => {
    setConflictPrefillAttempt((attempt) => attempt + 1)
  }, [])

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

  const handlePickFromCatalogue = useCallback(
    async (row: ProductsModalItem) => {
      const sanitizedEan = sanitizeScanValue(row.ean ?? row.codeDigits ?? '')
      if (!sanitizedEan) {
        setErrorMessage(`Impossible d’ajouter ${row.name} : code manquant.`)
        updateStatus(null)
        return false
      }

      if (sanitizedEan.length > MAX_SCAN_LENGTH) {
        setErrorMessage(`Code ${sanitizedEan} invalide : ${MAX_SCAN_LENGTH} caractères maximum.`)
        updateStatus(null)
        return false
      }

      try {
        ensureScanPrerequisites()
      } catch (error) {
        setErrorMessage(error instanceof Error ? error.message : 'Impossible d’ajouter ce produit.')
        updateStatus(null)
        return false
      }

      updateStatus(`Ajout de ${row.name}…`)
      setErrorMessage(null)

      try {
        const product = await fetchProductByEan(sanitizedEan)
        const added = await addProductToSession(
          { ...product, sku: product.sku ?? row.sku },
          { isManual: true },
        )
        if (!added) {
          updateStatus(null)
          return false
        }

        updateStatus(`${product.name} ajouté`)
        pendingFocusEanRef.current = product.ean ?? sanitizedEan
        return true
      } catch (error) {
        const err = error as HttpError
        if (err?.status === 404) {
          setErrorMessage(`Produit introuvable pour ${sanitizedEan}. Signalez ce code.`)
          triggerScanRejectionFeedback()
        } else {
          setErrorMessage('Impossible d’ajouter ce produit. Réessayez.')
        }
        updateStatus(null)
        return false
      }
    },
    [
      addProductToSession,
      ensureScanPrerequisites,
      triggerScanRejectionFeedback,
      updateStatus,
      setErrorMessage,
    ],
  )

  const handleUnknownProduct = useCallback(
    (code: string) => {
      updateStatus(null)
      setErrorMessage(
        `Code ${code} introuvable dans la liste des produits à inventorier.`,
      )
      setInputLookupStatus('not-found')
      setScanValue(code)
      triggerScanRejectionFeedback()
    },
    [triggerScanRejectionFeedback, updateStatus],
  )

  const handleDetected = useCallback(
    async (rawValue: string) => {
      const value = sanitizeScanValue(rawValue)
      if (!value) {
        return
      }

      if (value.length > MAX_SCAN_LENGTH) {
        updateStatus(null)
        setErrorMessage(`Code ${value} trop long : ${MAX_SCAN_LENGTH} caractères maximum.`)
        setInputLookupStatus('error')
        return
      }

      updateStatus(`Recherche du code ${value}`)
      setErrorMessage(null)

      const result = await searchProductByEan(value)

      if (result.status === 'found') {
        const product: Product = result.product
        const added = await addProductToSession(product)
        if (added) {
          updateStatus(`${product.name} ajouté`)
        }
        return
      }

      if (result.status === 'not-found') {
        handleUnknownProduct(value)
        return
      }

      const err = result.error
      updateStatus(null)
      setErrorMessage(
        resolveLifecycleErrorMessage(
          err,
          'Impossible de récupérer le produit. Réessayez ou signalez le code scanné.',
        ),
      )
      setInputLookupStatus('error')
    },
    [addProductToSession, handleUnknownProduct, searchProductByEan, updateStatus],
  )

  const normalizedScanValue = sanitizeScanValue(scanValue)
  const scanInputError = useMemo(() => {
    if (!normalizedScanValue) {
      return null
    }
    if (normalizedScanValue.length > MAX_SCAN_LENGTH) {
      return `Code ${normalizedScanValue} trop long : ${MAX_SCAN_LENGTH} caractères maximum.`
    }
    return null
  }, [normalizedScanValue])

  useEffect(() => {
    if (!normalizedScanValue) {
      manualLookupIdRef.current += 1
      lastSearchedInputRef.current = null
      setInputLookupStatus('idle')
      return
    }

    if (scanInputError) {
      manualLookupIdRef.current += 1
      lastSearchedInputRef.current = null
      setInputLookupStatus('error')
      setErrorMessage(scanInputError)
      return
    }

    if (lastSearchedInputRef.current === normalizedScanValue) {
      return
    }

    lastSearchedInputRef.current = normalizedScanValue
    const currentLookupId = ++manualLookupIdRef.current
    setInputLookupStatus('loading')
    updateStatus(`Recherche du code ${normalizedScanValue}`)
    setErrorMessage(null)

    const timeoutId = window.setTimeout(() => {
      void (async () => {
        const result = await searchProductByEan(normalizedScanValue)

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
          return
        }

        if (result.status === 'not-found') {
          handleUnknownProduct(normalizedScanValue)
          return
        }

        const err = result.error
        updateStatus(null)
        setErrorMessage(
          resolveLifecycleErrorMessage(
            err,
            'Impossible de récupérer le produit. Réessayez ou signalez le code scanné.',
          ),
        )
        setInputLookupStatus('error')
      })()
    }, 300)

    return () => {
      window.clearTimeout(timeoutId)
    }
  }, [
    addProductToSession,
    handleUnknownProduct,
    normalizedScanValue,
    scanInputError,
    searchProductByEan,
    updateStatus,
  ])

  const handleInputChange = useCallback(
    (event: ChangeEvent<HTMLInputElement>) => {
      const { value } = event.target
      if (value.includes('\n') || value.includes('\r')) {
        const code = sanitizeScanValue(value)
        setScanValue('')
        if (code) {
          void handleDetected(code)
        }
        return
      }
      const sanitized = sanitizeScanValue(value)
      setScanValue(sanitized)
      setErrorMessage(null)
    },
    [handleDetected],
  )

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
      const invalidEanLabels = collectInvalidEanLabels(items)
      if (invalidEanLabels.length > 0) {
        const summary = formatInvalidEanSummary(invalidEanLabels)
        throw new Error(
          `Impossible de terminer : certains articles ont un EAN invalide (${summary}). Corrigez-les ou supprimez-les puis réessayez.`,
        )
      }

      const payloadItems = aggregateItemsForCompletion(items)

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
      clearSession()
      setScanValue('')
      setInputLookupStatus('idle')
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
    const dialog = completionConfirmationDialogRef.current
    if (dialog && typeof dialog.close === 'function') {
      dialog.close()
    }
  }, [])

  const handleConfirmCompletionConfirmation = useCallback(() => {
    const dialog = completionConfirmationDialogRef.current
    if (dialog && typeof dialog.close === 'function') {
      dialog.close()
    }
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

  useEffect(() => {
    const targetEan = pendingFocusEanRef.current
    if (!targetEan) {
      return
    }

    const quantityInput = document.querySelector<HTMLInputElement>(
      `[data-ean="${targetEan}"] input[data-testid="quantity-input"]`,
    )
    if (quantityInput) {
      requestAnimationFrame(() => {
        quantityInput.focus({ preventScroll: true })
        quantityInput.select?.()
      })
    }

    pendingFocusEanRef.current = null
  }, [items])

  const handleCompletionModalOk = () => {
    completionDialogRef.current?.close()
    navigate('/', { replace: true })
  }

  const clearQuantityDraft = useCallback((ean: string) => {
    setQuantityDrafts((prev) => {
      if (!(ean in prev)) {
        return prev
      }
      const next = { ...prev }
      delete next[ean]
      return next
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

  const hasPositiveQuantity = useMemo(() => {
    return items.some((item) => {
      const draftValue = quantityDrafts[item.product.ean]
      if (typeof draftValue === 'string' && draftValue.length > 0) {
        const parsed = Number.parseInt(draftValue, 10)
        return Number.isFinite(parsed) && parsed > 0
      }
      return item.quantity > 0
    })
  }, [items, quantityDrafts])

  const canCompleteRun =
    locationId.length > 0 &&
    isValidCountType &&
    ownerUserId.length > 0 &&
    shopId.length > 0 &&
    items.length > 0 &&
    hasPositiveQuantity &&
    !completionLoading &&
    (!isConflictResolutionMode || conflictPrefillStatus === 'loaded')

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
        <div className="flex flex-wrap items-center gap-2 sm:justify-end">
          <Button
            type="button"
            variant="secondary"
            size="sm"
            onClick={handleOpenLogsDialog}
            aria-haspopup="dialog"
            data-testid="btn-open-logs"
          >
            Journal des actions ({logs.length})
          </Button>
          {!isConflictResolutionMode && (
            <>
              <Button
                type="button"
                variant="secondary"
                size="sm"
                onClick={() => navigate('/inventory/scan-camera')}
                data-testid="btn-scan-camera"
              >
                Scan caméra
              </Button>
              <Button
                type="button"
                variant="secondary"
                size="sm"
                onClick={() => setCatalogueOpen(true)}
                data-testid="btn-open-catalogue"
                disabled={!shopId}
              >
                Ajout via catalogue
              </Button>
            </>
          )}
        </div>
      </div>
        {isConflictResolutionMode ? (
          <div className="space-y-3 rounded-2xl border border-amber-200 bg-amber-50 p-4 text-sm text-amber-800 dark:border-amber-900/40 dark:bg-amber-900/20 dark:text-amber-100">
            <p className="font-semibold">Références en conflit</p>
            <p>
              Saisissez la quantité constatée pour chaque produit afin de valider ce comptage de concordance.
            </p>
            {conflictPrefillStatus === 'loading' && (
              <div className="pt-1">
                <LoadingIndicator label="Chargement des références en conflit" />
              </div>
            )}
            {conflictPrefillStatus === 'error' && (
              <div className="flex flex-wrap items-center gap-3 text-rose-700 dark:text-rose-300">
                <span>{conflictPrefillError ?? 'Impossible de charger les références en conflit.'}</span>
                <Button type="button" variant="ghost" onClick={handleRetryConflictPrefill}>
                  Réessayer
                </Button>
              </div>
            )}
          </div>
        ) : (
          <>
            <div className="space-y-2">
              <Input
                ref={inputRef}
                name="scanInput"
                label="Scanner (douchette ou saisie)"
                placeholder="Saisissez ou scannez un code EAN/RFID"
                value={scanValue}
                onChange={handleInputChange}
                inputMode="text"
                maxLength={MAX_SCAN_LENGTH}
                autoComplete="off"
                aria-invalid={Boolean(scanInputError)}
                autoFocus
              />
              {scanInputError && (
                <p className="text-xs text-rose-600 dark:text-rose-300" aria-live="polite">
                  {scanInputError}
                </p>
              )}
            </div>
          </>
        )}
        {status && (
          <p className="text-sm text-brand-600 dark:text-brand-200" data-testid="status-message">
            {status}
          </p>
        )}
        {errorMessage && <p className="text-sm text-red-600 dark:text-red-300">{errorMessage}</p>}
      </Card>

      <dialog
        ref={logsDialogRef}
        aria-modal="true"
        aria-labelledby="session-log-title"
        aria-describedby="session-log-description"
        className="px-4"
      >
        <Card className="w-full max-w-2xl shadow-elev-2">
          <div className="flex items-start justify-between gap-4">
            <div>
              <p id="session-log-title" className="text-lg font-semibold">
                Journal de session
              </p>
              <p id="session-log-description" className="text-sm text-slate-600 dark:text-slate-300">
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
        </Card>
      </dialog>

      <Card className="space-y-4">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div className="flex flex-col gap-1">
            <h3 className="text-xl font-semibold text-slate-900 dark:text-white">Articles scannés</h3>
            <span className="text-sm text-slate-600 dark:text-slate-400">{items.length} références</span>
          </div>
          <div className="flex flex-wrap items-center gap-3 sm:justify-end">
            {conflictZoneSummary && (
              <>
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
                  Voir les écarts
                </Button>
              </>
            )}
          </div>
        </div>
        {displayedItems.length === 0 && (!isConflictResolutionMode || conflictPrefillStatus === 'loaded') && (
          <EmptyState
            title={isConflictResolutionMode ? 'Aucune référence en conflit' : 'En attente de scan'}
            description={
              isConflictResolutionMode
                ? 'Toutes les divergences ont été résolues pour cette zone.'
                : "Scannez un produit via la caméra ou la douchette pour l'ajouter au comptage."
            }
          />
        )}
        <ul className="flex flex-col gap-3">
          {displayedItems.map((item) => {
            const skuDisplay = formatIdentifierForDisplay(item.product.sku)
            const eanDisplay = formatIdentifierForDisplay(item.product.ean)
            const subGroupDisplay = normalizeIdentifier(item.product.subGroup)
            const metadataSegments = [
              subGroupDisplay ? `Sous-groupe ${subGroupDisplay}` : null,
              `SKU ${skuDisplay}`,
              `EAN ${eanDisplay}`,
            ].filter((segment): segment is string => Boolean(segment))

            return (
              <li
                key={item.id}
                className="flex flex-col gap-4 rounded-3xl border border-slate-200 bg-white p-4 shadow-sm transition-colors dark:border-slate-700 dark:bg-slate-900/60"
                data-testid="scanned-item"
                data-item-id={item.id}
                data-ean={item.product.ean}
                data-sku={item.product.sku ?? ''}
              >
                <div className="flex flex-col gap-2">
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <p className="text-base font-semibold text-slate-900 dark:text-white sm:text-lg">
                      {item.product.name}
                    </p>
                    {item.hasConflict && (
                      <span className="inline-flex items-center rounded-full bg-rose-100 px-2 py-1 text-[11px] font-semibold uppercase tracking-wide text-rose-700 dark:bg-rose-900/60 dark:text-rose-200">
                        Référence en conflit
                      </span>
                    )}
                  </div>
                  <div className="flex flex-wrap gap-x-3 gap-y-1 text-[11px] font-medium uppercase tracking-wide text-slate-400 dark:text-slate-500 sm:text-xs">
                    {metadataSegments.map((segment) => (
                      <span key={segment}>{segment}</span>
                    ))}
                  </div>
                </div>
                <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
                  <span className="text-[11px] font-semibold uppercase tracking-[0.25em] text-slate-400 dark:text-slate-500 sm:hidden">
                    Quantité
                  </span>
                  <div className="flex items-center justify-between gap-2 rounded-2xl bg-slate-100 p-2 shadow-inner sm:bg-transparent sm:p-0 sm:shadow-none">
                    <Button
                      type="button"
                      variant="secondary"
                      className="tap-highlight-none no-focus-ring btn-glyph-center h-11 w-11 text-xl font-semibold sm:h-10 sm:w-10"
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
                      className="h-12 w-20 rounded-xl border border-slate-300 bg-white text-center text-2xl font-bold text-slate-900 shadow-sm outline-none focus-visible:border-brand-400 focus-visible:ring-2 focus-visible:ring-brand-200 dark:border-slate-600 dark:bg-slate-800 dark:text-white"
                      autoComplete="off"
                    />
                    <Button
                      type="button"
                      className="tap-highlight-none no-focus-ring btn-glyph-center h-11 w-11 text-xl font-semibold sm:h-10 sm:w-10"
                      onClick={() => adjustQuantity(item.product.ean, 1)}
                      aria-label="Ajouter"
                    >
                      +
                    </Button>
                  </div>
                </div>
              </li>
            )
          })}
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
        aria-labelledby="completion-confirmation-title"
        aria-describedby="completion-confirmation-description"
        className="px-4"
      >
        <Card className="w-full max-w-lg shadow-elev-2">
          <div className="space-y-4">
            <p id="completion-confirmation-title" className="text-lg font-semibold">
              Confirmer la clôture du comptage
            </p>
            <p id="completion-confirmation-description" className="text-sm text-slate-600 dark:text-slate-300">
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
        </Card>
      </dialog>
      <dialog
        ref={completionDialogRef}
        id="complete-inventory-modal"
        aria-modal="true"
        aria-labelledby="completion-success-title"
        className="px-4"
      >
        <Card className="w-full max-w-lg shadow-elev-2">
          <p id="completion-success-title" className="text-lg font-semibold">
            Le comptage a été enregistré avec succès.
          </p>
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
        </Card>
      </dialog>
      <ConflictZoneModal
        open={Boolean(conflictZoneSummary) && conflictModalOpen}
        zone={conflictZoneSummary}
        onClose={() => setConflictModalOpen(false)}
      />
      <ProductsModal
        open={catalogueOpen && Boolean(shopId)}
        onClose={() => setCatalogueOpen(false)}
        shopId={shopId}
        onSelect={handlePickFromCatalogue}
      />

    </div>
  )
}
