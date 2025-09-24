import { BrowserMultiFormatReader } from '@zxing/browser'
import type { IScannerControls } from '@zxing/browser'
import type { Result } from '@zxing/library'
import { useEffect, useRef, useState, type ChangeEvent } from 'react'

interface BarcodeScannerProps {
  active: boolean
  onDetected: (value: string) => void
  onPickImage?: (file: File) => void
}

const isCameraAvailable = () => {
  if (typeof window === 'undefined' || typeof navigator === 'undefined') {
    return false
  }
  if (!window.isSecureContext) {
    return false
  }
  if (!('mediaDevices' in navigator)) {
    return false
  }
  return typeof navigator.mediaDevices?.getUserMedia === 'function'
}

export const BarcodeScanner = ({ active, onDetected, onPickImage }: BarcodeScannerProps) => {
  const videoRef = useRef<HTMLVideoElement | null>(null)
  const [error, setError] = useState<string | null>(null)
  const cameraAvailable = isCameraAvailable()

  useEffect(() => {
    if (!active) {
      setError(null)
    }
  }, [active])

  useEffect(() => {
    if (!active || !videoRef.current) {
      return
    }
    if (!cameraAvailable) {
      return
    }
    const reader = new BrowserMultiFormatReader()
    let isMounted = true

    let controls: IScannerControls | null = null

    const constraints: MediaStreamConstraints = { video: { facingMode: { ideal: 'environment' } } }

    setError(null)

    reader
      .decodeFromConstraints(constraints, videoRef.current, (result: Result | undefined, err) => {
        if (!isMounted) {
          return
        }
        if (result) {
          onDetected(result.getText())
        }
        if (err?.name === 'NotAllowedError') {
          setError('Accès caméra refusé. Autorisez l’accès à la caméra dans votre navigateur.')
        } else if (err?.name === 'NotFoundError') {
          setError('Aucune caméra compatible détectée. Branchez ou choisissez un autre appareil.')
        }
      })
      .then((scannerControls) => {
        controls = scannerControls
      })
      .catch((cameraError) => {
        if (cameraError instanceof Error) {
          setError(cameraError.message)
        } else {
          setError('Caméra indisponible')
        }
      })

    return () => {
      isMounted = false
      controls?.stop()
    }
  }, [active, cameraAvailable, onDetected])

  const handleImageChange = (event: ChangeEvent<HTMLInputElement>) => {
    if (!onPickImage) {
      return
    }
    const [file] = Array.from(event.target.files ?? [])
    if (file) {
      onPickImage(file)
    }
    event.target.value = ''
  }

  return (
    <div className="relative overflow-hidden rounded-3xl border border-slate-700 bg-black/60">
      {cameraAvailable ? (
        <>
          <video ref={videoRef} className="h-64 w-full object-cover" muted playsInline autoPlay />
          <div className="pointer-events-none absolute inset-0 border-4 border-dashed border-brand-400/60" aria-hidden />
          {error && <p className="p-4 text-sm text-red-200">{error}</p>}
          {!error && !active && (
            <p className="p-4 text-sm text-slate-400">Activez la caméra pour scanner un code-barres.</p>
          )}
        </>
      ) : (
        <div className="space-y-3 p-4">
          <p className="text-sm font-semibold text-amber-200">
            Caméra indisponible (connexion non sécurisée ou navigateur incompatible).
          </p>
          <ul className="list-disc space-y-1 pl-5 text-sm text-slate-200">
            <li>Utilisez une URL en https</li>
            <li>Autorisez l’accès caméra dans le navigateur</li>
            <li>Sinon, importez une photo du code-barres ci-dessous</li>
          </ul>
          {onPickImage && (
            <label className="flex cursor-pointer flex-col gap-2 rounded-2xl border border-slate-500 bg-slate-800/40 p-3 text-sm text-slate-100 hover:border-brand-400">
              <span className="font-medium">Importer une photo du code-barres</span>
              <input
                type="file"
                accept="image/*"
                capture="environment"
                className="text-xs text-slate-300"
                onChange={handleImageChange}
              />
            </label>
          )}
        </div>
      )}
    </div>
  )
}
