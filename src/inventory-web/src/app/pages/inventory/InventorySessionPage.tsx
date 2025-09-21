import { isAxiosError } from 'axios'
import type { FormEvent, KeyboardEvent } from 'react'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { createManualProduct, fetchProductByEan } from '../../api/inventoryApi'
import { BarcodeScanner } from '../../components/BarcodeScanner'
import { Button } from '../../components/Button'
import { Card } from '../../components/Card'
import { EmptyState } from '../../components/EmptyState'
import { SlidingPanel } from '../../components/SlidingPanel'
import { TextField } from '../../components/TextField'
import { useInventory } from '../../contexts/InventoryContext'

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
      try {
        const product = await fetchProductByEan(value)
        addOrIncrementItem(product)
        setStatus(`${product.name} ajouté`)
      } catch (error) {
        if (isAxiosError(error) && error.response?.status === 404) {
          setStatus(null)
          setManualEan(value)
          setManualOpen(true)
        } else {
          setErrorMessage('Impossible de récupérer le produit. Réessayez ou ajoutez-le manuellement.')
        }
      }
    },
    [addOrIncrementItem],
  )

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
        setErrorMessage("Échec de la création du produit. Vérifiez l'EAN et réessayez.")
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
          <h2 className="text-2xl font-semibold text-white">Session de comptage</h2>
          <p className="text-sm text-slate-400">
            {location?.name} • {countType} comptage{countType && countType > 1 ? 's' : ''} • {selectedUser}
          </p>
          {sessionId && <p className="text-xs text-slate-500">Session existante #{sessionId}</p>}
        </div>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <Button variant="secondary" onClick={() => setUseCamera((prev) => !prev)}>
            {useCamera ? 'Désactiver la caméra' : 'Activer la caméra'}
          </Button>
          <Button variant="ghost" onClick={() => setManualOpen(true)}>
            Ajouter manuellement
          </Button>
        </div>
        <BarcodeScanner active={useCamera} onDetected={handleDetected} />
        <TextField
          ref={inputRef}
          label="Scanner (douchette ou saisie)"
          placeholder="Scannez un EAN et validez avec Entrée"
          onKeyDown={handleInputKeyDown}
          autoFocus
        />
        {status && <p className="text-sm text-brand-200">{status}</p>}
        {errorMessage && <p className="text-sm text-red-300">{errorMessage}</p>}
      </Card>

      <Card className="space-y-4">
        <div className="flex items-center justify-between">
          <h3 className="text-xl font-semibold text-white">Articles scannés</h3>
          <span className="text-sm text-slate-400">{items.length} références</span>
        </div>
        {sortedItems.length === 0 && (
          <EmptyState
            title="En attente de scan"
            description="Scannez un produit via la caméra ou la douchette pour l&apos;ajouter au comptage."
          />
        )}
        <ul className="flex flex-col gap-3">
          {sortedItems.map((item) => (
            <li key={item.product.ean} className="flex items-center justify-between rounded-2xl bg-slate-900/60 p-4">
              <div>
                <p className="text-lg font-semibold text-white">{item.product.name}</p>
                <p className="text-xs text-slate-400">EAN {item.product.ean}</p>
                {item.isManual && <p className="text-xs text-amber-300">Ajout manuel</p>}
              </div>
              <div className="flex items-center gap-3">
                <button
                  type="button"
                  className="h-10 w-10 rounded-full bg-slate-800 text-xl text-white"
                  onClick={() => adjustQuantity(item.product.ean, -1)}
                  aria-label={`Retirer ${item.product.name}`}
                >
                  –
                </button>
                <span className="text-2xl font-bold text-white">{item.quantity}</span>
                <button
                  type="button"
                  className="h-10 w-10 rounded-full bg-brand-600 text-xl text-white"
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
          <TextField label="EAN" value={manualEan} onChange={(event) => setManualEan(event.target.value)} />
          <TextField label="Libellé" value={manualName} onChange={(event) => setManualName(event.target.value)} />
          <Button type="submit" fullWidth disabled={manualLoading} className="py-4">
            {manualLoading ? 'Création…' : 'Ajouter à la session'}
          </Button>
        </form>
      </SlidingPanel>
    </div>
  )
}
