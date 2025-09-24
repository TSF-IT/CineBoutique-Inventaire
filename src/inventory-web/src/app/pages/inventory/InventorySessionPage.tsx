import type { FormEvent, KeyboardEvent } from 'react'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { BrowserMultiFormatReader } from '@zxing/browser'
import { BarcodeFormat, DecodeHintType } from '@zxing/library'
import { createManualProduct, fetchProductByEan } from '../../api/inventoryApi'
import { BarcodeScanner } from '../../components/BarcodeScanner'
import { Button } from '../../components/ui/Button'
import { Input } from '../../components/ui/Input'
import { Card } from '../../components/Card'
import { EmptyState } from '../../components/EmptyState'
import { SlidingPanel } from '../../components/SlidingPanel'
import { useInventory } from '../../contexts/InventoryContext'
import type { HttpError } from '@/lib/api/http'

const DEV_API_UNREACHABLE_HINT =
  "Impossible de joindre l’API : vérifie que le backend tourne (curl http://localhost:8080/healthz) ou que le proxy Vite est actif."

const isHttpError = (value: unknown): value is HttpError =>
  typeof value === 'object' &&
  value !== null &&
  typeof (value as { status?: unknown }).status === 'number' &&
  typeof (value as { url?: unknown }).url === 'string'

const buildHttpMessage = (prefix: string, error: HttpError) => {
  if (import.meta.env.DEV && error.status === 404) {
    const diagnostics = [DEV_API_UNREACHABLE_HINT]
    if (error.url) {
      diagnostics.push(`URL: ${error.url}`)
    }
    const detail =
      (error.problem as { detail?: string; title?: string } | undefined)?.detail ||
      (error.problem as { title?: string } | undefined)?.title ||
      error.body
    if (detail) {
      diagnostics.push(`Détail: ${detail}`)
    }
    return diagnostics.join(' | ')
  }

  const diagnostics: string[] = []
  if (typeof error.status === 'number') {
    diagnostics.push(`HTTP ${error.status}`)
  }
  if (error.url) {
    diagnostics.push(`URL: ${error.url}`)
  }
  const detail =
    (error.problem as { detail?: string; title?: string } | undefined)?.detail ||
    (error.problem as { title?: string } | undefined)?.title ||
    error.body
  if (detail) {
    diagnostics.push(`Détail: ${detail}`)
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
  } = useInventory()
  const [useCamera, setUseCamera] = useState(false)
  const [status, setStatus] = useState<string | null>(null)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [manualOpen, setManualOpen] = useState(false)
  const [manualEan, setManualEan] = useState('')
  const [manualName, setManualName] = useState('')
  const [manualLoading, setManualLoading] = useState(false)
  const inputRef = useRef<HTMLInputElement | null>(null)
  const hidInputRef = useRef<HTMLInputElement | null>(null)
  const hidBufferRef = useRef('')
  const hidResetTimeoutRef = useRef<number | null>(null)
  const [recentScans, setRecentScans] = useState<string[]>([])

  useEffect(() => {
    if (!selectedUser) {
      navigate('/inventory/start', { replace: true })
    } else if (!countType) {
      navigate('/inventory/count-type', { replace: true })
    } else if (!location) {
      navigate('/inventory/location', { replace: true })
    }
  }, [countType, location, navigate, selectedUser])

  useEffect(() => {
    if (!manualOpen) {
      inputRef.current?.focus()
    }
  }, [manualOpen])

  useEffect(() => {
    const hiddenInput = hidInputRef.current
    if (!hiddenInput) {
      return
    }
    const focusTarget = () => {
      if (document.activeElement !== hiddenInput && !manualOpen) {
        hiddenInput.focus()
      }
    }
    hiddenInput.focus()
    const handleBlur = () => {
      window.setTimeout(focusTarget, 50)
    }
    hiddenInput.addEventListener('blur', handleBlur)
    const interval = window.setInterval(focusTarget, 4000)
    return () => {
      hiddenInput.removeEventListener('blur', handleBlur)
      window.clearInterval(interval)
    }
  }, [manualOpen])

  useEffect(() => () => {
    if (hidResetTimeoutRef.current) {
      window.clearTimeout(hidResetTimeoutRef.current)
      hidResetTimeoutRef.current = null
    }
  }, [])

  const sortedItems = useMemo(
    () => [...items].sort((a, b) => b.lastScanAt.localeCompare(a.lastScanAt)),
    [items],
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
      try {
        const product = await fetchProductByEan(value)
        addOrIncrementItem(product)
        setStatus(`${product.name} ajouté`)
      } catch (error) {
        const err = error as HttpError
        if (isHttpError(err) && err.status === 404) {
          setStatus(null)
          setManualEan(value)
          setManualOpen(true)
        } else {
          if (isHttpError(err)) {
            setErrorMessage(buildHttpMessage('Erreur réseau', err))
          } else {
            setErrorMessage('Impossible de récupérer le produit. Réessayez ou ajoutez-le manuellement.')
          }
        }
      }
    },
    [addOrIncrementItem],
  )

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

  const flushHidBuffer = useCallback(() => {
    if (hidResetTimeoutRef.current) {
      window.clearTimeout(hidResetTimeoutRef.current)
      hidResetTimeoutRef.current = null
    }
    const value = hidBufferRef.current.trim()
    hidBufferRef.current = ''
    if (value) {
      void handleDetected(value)
    }
  }, [handleDetected])

  const handleInputKeyDown = useCallback(
    (event: KeyboardEvent<HTMLInputElement>) => {
      if (event.key === 'Enter') {
        event.preventDefault()
        const target = event.target as HTMLInputElement
        const value = target.value.trim()
        target.value = ''
        void handleDetected(value)
      }
    },
    [handleDetected],
  )

  const handleHidKeyDown = useCallback(
    (event: KeyboardEvent<HTMLInputElement>) => {
      if (event.key === 'Enter') {
        event.preventDefault()
        flushHidBuffer()
        return
      }
      if (event.key.length === 1) {
        hidBufferRef.current = `${hidBufferRef.current}${event.key}`
        if (hidResetTimeoutRef.current) {
          window.clearTimeout(hidResetTimeoutRef.current)
        }
        hidResetTimeoutRef.current = window.setTimeout(() => {
          hidBufferRef.current = ''
        }, 120)
      }
    },
    [flushHidBuffer],
  )

  const handleManualSubmit = useCallback(
    async (event: FormEvent<HTMLFormElement>) => {
      event.preventDefault()
      if (!manualEan.trim() || !manualName.trim()) {
        setErrorMessage('Indiquez un EAN et un libellé pour créer le produit.')
        return
      }
      setManualLoading(true)
      setErrorMessage(null)
      try {
        const product = await createManualProduct({ ean: manualEan.trim(), name: manualName.trim() })
        addOrIncrementItem(product, { isManual: true })
        setStatus(`${product.name} ajouté manuellement`)
        setManualOpen(false)
        setManualName('')
        setManualEan('')
      } catch (error) {
        const err = error as HttpError
        if (isHttpError(err)) {
          setErrorMessage(buildHttpMessage('Création impossible', err))
        } else {
          setErrorMessage("Échec de la création du produit. Vérifiez l'EAN et réessayez.")
        }
      } finally {
        setManualLoading(false)
      }
    },
    [addOrIncrementItem, manualEan, manualName],
  )

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
    <div className="flex flex-col gap-6">
      <Card className="space-y-4">
        <div className="flex flex-col gap-2">
          <h2 className="text-2xl font-semibold text-slate-900 dark:text-white">Session de comptage</h2>
          <p className="text-sm text-slate-600 dark:text-slate-400">
            {location?.label} • {countType} comptage{countType && countType > 1 ? 's' : ''} • {selectedUser}
          </p>
          {sessionId && <p className="text-xs text-slate-500 dark:text-slate-400">Session existante #{sessionId}</p>}
        </div>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <Button variant="secondary" onClick={() => setUseCamera((prev) => !prev)}>
            {useCamera ? 'Désactiver la caméra' : 'Activer la caméra'}
          </Button>
          <Button variant="ghost" onClick={() => setManualOpen(true)}>
            Ajouter manuellement
          </Button>
        </div>
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
        <Input
          ref={inputRef}
          name="scanInput"
          label="Scanner (douchette ou saisie)"
          placeholder="Scannez un EAN et validez avec Entrée"
          onKeyDown={handleInputKeyDown}
          autoFocus
        />
        <input
          ref={hidInputRef}
          type="text"
          aria-hidden
          className="absolute left-[-9999px] top-auto h-0 w-0 opacity-0"
          onKeyDown={handleHidKeyDown}
        />
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
      </Card>

      <SlidingPanel open={manualOpen} title="Ajouter un produit" onClose={() => setManualOpen(false)}>
        <form className="space-y-4" onSubmit={handleManualSubmit}>
          <Input
            label="EAN"
            name="manualEan"
            value={manualEan}
            onChange={(event) => setManualEan(event.target.value)}
          />
          <Input
            label="Libellé"
            name="manualLabel"
            value={manualName}
            onChange={(event) => setManualName(event.target.value)}
          />
          <Button type="submit" fullWidth disabled={manualLoading} className="py-4">
            {manualLoading ? 'Création…' : 'Ajouter à la session'}
          </Button>
        </form>
      </SlidingPanel>
    </div>
  )
}
