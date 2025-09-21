import { BrowserMultiFormatReader } from '@zxing/browser'
import type { IScannerControls } from '@zxing/browser'
import type { Result } from '@zxing/library'
import { useEffect, useRef, useState } from 'react'

interface BarcodeScannerProps {
  active: boolean
  onDetected: (value: string) => void
}

export const BarcodeScanner = ({ active, onDetected }: BarcodeScannerProps) => {
  const videoRef = useRef<HTMLVideoElement | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!active || !videoRef.current) {
      return
    }
    const reader = new BrowserMultiFormatReader()
    let isMounted = true

    let controls: IScannerControls | null = null

    reader
      .decodeFromVideoDevice(undefined, videoRef.current, (result: Result | undefined, err) => {
        if (!isMounted) {
          return
        }
        if (result) {
          onDetected(result.getText())
        }
        if (err?.name === 'NotAllowedError') {
          setError('Accès caméra refusé. Activez l\'autorisation dans votre navigateur.')
        }
      })
      .then((scannerControls) => {
        controls = scannerControls
      })
      .catch((cameraError) => {
        setError(cameraError instanceof Error ? cameraError.message : 'Caméra indisponible')
      })

    return () => {
      isMounted = false
      controls?.stop()
    }
  }, [active, onDetected])

  return (
    <div className="relative overflow-hidden rounded-3xl border border-slate-700 bg-black/60">
      <video ref={videoRef} className="h-64 w-full object-cover" muted playsInline autoPlay />
      <div className="pointer-events-none absolute inset-0 border-4 border-dashed border-brand-400/60" aria-hidden />
      {error && <p className="p-4 text-sm text-red-200">{error}</p>}
      {!error && !active && (
        <p className="p-4 text-sm text-slate-400">Activez la caméra pour scanner un code-barres.</p>
      )}
    </div>
  )
}
