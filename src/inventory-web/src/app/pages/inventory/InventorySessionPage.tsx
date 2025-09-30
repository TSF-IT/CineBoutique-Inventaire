import type { KeyboardEvent, ChangeEvent } from 'react'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { BrowserMultiFormatReader } from '@zxing/browser'
import { BarcodeFormat, DecodeHintType } from '@zxing/library'
import {
  completeInventoryRun,
  fetchProductByEan,
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
    clearSession,
  } = useInventory()
  const [useCamera, setUseCamera] = useState(false)
  const [status, setStatus] = useState<string | null>(null)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [scanValue, setScanValue] = useState('')
  const [manualEan, setManualEan] = useState('')
  const [inputLookupStatus, setInputLookupStatus] = useState<'idle' | 'loading' | 'found' | 'not-found' | 'error'>('idle')
  const [completionLoading, setCompletionLoading] = useState(false)
  const completionDialogRef = useRef<HTMLDialogElement | null>(null)
  const completionOkButtonRef = useRef<HTMLButtonElement | null>(null)
  const inputRef = useRef<HTMLInputElement | null>(null)
  const [recentScans, setRecentScans] = useState<string[]>([])
  const manualLookupIdRef = useRef(0)
  const lastSearchedInputRef = useRef<string | null>(null)

  useEffect(() => {
    if (!selectedUser) {
      navigate('/inventory/start', { replace: true })
    } else if (!location) {
      navigate('/inventory/location', { replace: true })
    } else if (!countType) {
      navigate('/inventory/count-type', { replace: true })
    }
  }, [countType, location, navigate, selectedUser])

  const sortedItems = useMemo(
    () => [...items].sort((a, b) => b.lastScanAt.localeCompare(a.lastScanAt)),
    [items],
  )

  const searchProductByEan = useCallback(async (ean: string) => {
    try {
      const product = await fetchProductByEan(ean)
      return { status: 'found' as const, product }
    } catch (error) {
      const err = error as HttpError
      if (isHttpError(err) && err.status === 404) {
        return { status: 'not-found' as const, error: err }
      }
      return { status: 'error' as const, error: err }
    }
  }, [])

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
        addOrIncrementItem(product)
        setStatus(`${product.name} ajouté`)
        return
      }

      if (result.status === 'not-found') {
        setStatus(null)
        setManualEan(value)
        setInputLookupStatus('not-found')
        return
      }

      const err = result.error
      if (isHttpError(err)) {
        setErrorMessage(buildHttpMessage('Erreur réseau', err))
      } else {
        setErrorMessage('Impossible de récupérer le produit. Réessayez ou ajoutez-le manuellement.')
      }
    },
    [addOrIncrementItem, searchProductByEan],
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
          addOrIncrementItem(product)
          setStatus(`${product.name} ajouté`)
          setScanValue('')
          setInputLookupStatus('found')
        } else if (result.status === 'not-found') {
          setStatus(null)
          setManualEan(trimmedScanValue)
          setErrorMessage('Aucun produit trouvé pour cet EAN. Ajoutez-le manuellement.')
          setInputLookupStatus('not-found')
        } else {
          const err = result.error
          if (isHttpError(err)) {
            setErrorMessage(buildHttpMessage('Erreur réseau', err))
          } else {
            setErrorMessage('Impossible de récupérer le produit. Réessayez ou ajoutez-le manuellement.')
          }
          setInputLookupStatus('error')
        }
      })()
    }, 300)

    return () => {
      window.clearTimeout(timeoutId)
    }
  }, [addOrIncrementItem, searchProductByEan, trimmedScanValue])

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

  const handleManualAdd = useCallback(() => {
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

    setErrorMessage(null)
    addOrIncrementItem(product, { isManual: true })
    setStatus(`${product.name} ajouté manuellement`)
    setManualEan('')
    setScanValue('')
    setInputLookupStatus('idle')
    inputRef.current?.focus()
  }, [addOrIncrementItem, manualCandidateEan])

  const trimmedOperator = selectedUser?.trim() ?? ''
  const isValidCountType =
    countType === CountType.Count1 || countType === CountType.Count2 || countType === CountType.Count3
  const canCompleteRun =
    Boolean(location) &&
    isValidCountType &&
    trimmedOperator.length > 0 &&
    items.length > 0 &&
    !completionLoading

  const handleCompleteRun = useCallback(async () => {
    if (!location || !isValidCountType || trimmedOperator.length === 0 || items.length === 0) {
      return
    }
    setCompletionLoading(true)
    setErrorMessage(null)
    setStatus('Envoi du comptage…')
    try {
      const payload: CompleteInventoryRunPayload = {
        countType: countType as 1 | 2 | 3,
        operator: trimmedOperator,
        items: items.map((item) => ({
          ean: item.product.ean,
          quantity: item.quantity,
          isManual: Boolean(item.isManual),
        })),
      }

      await completeInventoryRun(location.id, payload)
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
  }, [clearSession, countType, isValidCountType, items, location, navigate, trimmedOperator])

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
            {location?.label} • {countType} comptage{countType && countType > 1 ? 's' : ''} • {selectedUser}
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
              handleManualAdd()
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
        {sortedItems.length === 0 && (
          <EmptyState
            title="En attente de scan"
            description="Scannez un produit via la caméra ou la douchette pour l&apos;ajouter au comptage."
          />
        )}
        <ul className="flex flex-col gap-3">
          {sortedItems.map((item) => (
            <li
              key={item.product.ean}
              className="flex items-center justify-between rounded-2xl border border-slate-200 bg-white p-4 dark:border-slate-700 dark:bg-slate-900/60"
            >
              <div>
                <p className="text-lg font-semibold text-slate-900 dark:text-white">{item.product.name}</p>
                <p className="text-xs text-slate-500 dark:text-slate-400">EAN {item.product.ean}</p>
                {item.isManual && <p className="text-xs text-amber-600 dark:text-amber-300">Ajout manuel</p>}
              </div>
              <div className="flex items-center gap-3">
                <button
                  type="button"
                  className="h-10 w-10 rounded-full border border-slate-300 bg-slate-100 text-xl text-slate-700 dark:border-slate-600 dark:bg-slate-800 dark:text-white"
                  onClick={() => adjustQuantity(item.product.ean, -1)}
                  aria-label={`Retirer ${item.product.name}`}
                >
                  –
                </button>
                <span className="text-2xl font-bold text-slate-900 dark:text-white">{item.quantity}</span>
                <button
                  type="button"
                  className="h-10 w-10 rounded-full bg-brand-600 text-xl text-white dark:bg-brand-500"
                  onClick={() => adjustQuantity(item.product.ean, 1)}
                  aria-label={`Ajouter ${item.product.name}`}
                >
                  +
                </button>
              </div>
            </li>
          ))}
        </ul>
        {sortedItems.length > 0 && (
          <div className="flex justify-end">
            <Button
              data-testid="btn-complete-run"
              className="py-3"
              disabled={!canCompleteRun}
              aria-disabled={!canCompleteRun}
              onClick={() => {
                void handleCompleteRun()
              }}
            >
              {completionLoading ? 'Enregistrement…' : 'Terminer ce comptage'}
            </Button>
          </div>
        )}
      </Card>
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
