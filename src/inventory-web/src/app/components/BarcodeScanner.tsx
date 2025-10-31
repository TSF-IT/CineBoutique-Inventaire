import { BrowserMultiFormatReader, type IScannerControls } from '@zxing/browser'
import { BarcodeFormat, DecodeHintType, NotFoundException } from '@zxing/library'
import clsx from 'clsx'
import {
  useCallback, useEffect, useMemo, useRef, useState,
  type ChangeEvent, type MutableRefObject,
} from 'react'

type SupportedFormat = 'EAN_13' | 'EAN_8' | 'CODE_128' | 'CODE_39' | 'ITF' | 'QR_CODE'
type DetectorFormat = 'ean_13' | 'ean_8' | 'code_128' | 'code_39' | 'itf' | 'qr_code'
type TorchCapabilities = MediaTrackCapabilities & { torch?: boolean }
type TorchConstraintSet = MediaTrackConstraintSet & { torch?: boolean }

type ExternalCameraBinding = {
  videoRef: MutableRefObject<HTMLVideoElement | null>
  active: boolean
  error?: unknown
}

interface BarcodeScannerProps {
  active: boolean
  onDetected: (value: string) => void | Promise<void>
  onError?: (reason: string) => void
  onPickImage?: (file: File) => void
  enableTorchToggle?: boolean
  preferredFormats?: SupportedFormat[]
  presentation?: 'embedded' | 'immersive'
  /** Si fourni, on n’ouvre/ferme jamais la caméra ici : on se contente de lire le flux. */
  camera?: ExternalCameraBinding
}

const DEFAULT_FORMATS: SupportedFormat[] = ['EAN_13','EAN_8','CODE_128','CODE_39','ITF','QR_CODE']
const ZXING_FORMATS: Record<SupportedFormat, BarcodeFormat> = {
  EAN_13: BarcodeFormat.EAN_13, EAN_8: BarcodeFormat.EAN_8, CODE_128: BarcodeFormat.CODE_128,
  CODE_39: BarcodeFormat.CODE_39, ITF: BarcodeFormat.ITF, QR_CODE: BarcodeFormat.QR_CODE,
}
const DETECTOR_FORMATS: Record<SupportedFormat, DetectorFormat> = {
  EAN_13: 'ean_13', EAN_8: 'ean_8', CODE_128: 'code_128',
  CODE_39: 'code_39', ITF: 'itf', QR_CODE: 'qr_code',
}
const HELP_TIMEOUT_MS = 9000

const isSecureCameraAvailable = () =>
  typeof window !== 'undefined' &&
  typeof navigator !== 'undefined' &&
  window.isSecureContext &&
  !!navigator.mediaDevices?.getUserMedia

const createHints = (formats: SupportedFormat[]) => {
  const hints = new Map()
  hints.set(DecodeHintType.POSSIBLE_FORMATS, formats.map((f) => ZXING_FORMATS[f]))
  hints.set(DecodeHintType.TRY_HARDER, true)
  return hints
}

export function BarcodeScanner({
  active,
  onDetected,
  onError,
  onPickImage,
  enableTorchToggle = true,
  preferredFormats,
  presentation = 'embedded',
  camera,
}: BarcodeScannerProps) {
  const internalVideoRef = useRef<HTMLVideoElement | null>(null)
  const videoRef = camera?.videoRef ?? internalVideoRef
  const usingExternalCamera = Boolean(camera)
  const externalActive = camera?.active ?? false
  const externalError = camera?.error

  const [status, setStatus] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [torchSupported, setTorchSupported] = useState(false)
  const [torchEnabled, setTorchEnabled] = useState(false)
  const [showHelp, setShowHelp] = useState(false)

  const controlsRef = useRef<IScannerControls | null>(null)
  const readerRef = useRef<BrowserMultiFormatReader | null>(null)
  const rafRef = useRef<number | null>(null)
  const helpTimeoutRef = useRef<number | null>(null)
  const processingRef = useRef(false)
  const restartRef = useRef<(() => Promise<void>) | null>(null)
  const activeRef = useRef(false)
  const lastZXLogRef = useRef(0)
  const lastLoopRef = useRef(0)

  const formats = useMemo(() => {
    const list = (preferredFormats?.length ? preferredFormats : DEFAULT_FORMATS)
    return Array.from(new Set(list))
  }, [preferredFormats])
  const hints = useMemo(() => createHints(formats), [formats])

  const cancelRaf = useCallback(() => {
    if (rafRef.current !== null) { cancelAnimationFrame(rafRef.current); rafRef.current = null }
  }, [])

  const stopZXing = useCallback(() => {
    controlsRef.current?.stop()
    controlsRef.current = null
    readerRef.current = null
  }, [])

  const cleanup = useCallback(() => {
    cancelRaf()
    stopZXing()
    if (helpTimeoutRef.current) { clearTimeout(helpTimeoutRef.current); helpTimeoutRef.current = null }
    restartRef.current = null
    processingRef.current = false
  }, [cancelRaf, stopZXing])

  const dispatchError = useCallback((message: string) => {
    setError(message)
    onError?.(message)
  }, [onError])

  const getCurrentStream = useCallback(() => {
    const el = videoRef.current
    const src = el && (el as any).srcObject
    return (src && src instanceof MediaStream) ? (src as MediaStream) : null
  }, [videoRef])

  const setTorch = useCallback(async (on: boolean) => {
    const stream = getCurrentStream()
    const [track] = stream?.getVideoTracks() ?? []
    if (!track?.applyConstraints) return false
    try {
      await track.applyConstraints({ advanced: [{ torch: on } as TorchConstraintSet] })
      setTorchEnabled(on)
      return true
    } catch {
      dispatchError('Activation de la lampe impossible sur cet appareil.')
      return false
    }
  }, [dispatchError, getCurrentStream])

  const scheduleHelp = useCallback(() => {
    if (helpTimeoutRef.current) clearTimeout(helpTimeoutRef.current)
   ;(helpTimeoutRef.current as any) = window.setTimeout(() => {
      setShowHelp(true)
      setStatus('Aucune détection, rapprochez‑vous ou améliorez l’éclairage.')
    }, HELP_TIMEOUT_MS)
  }, [])

  const handleDetectedValue = useCallback(async (value: string, source: 'detector' | 'zxing') => {
    if (!value || processingRef.current) return
    processingRef.current = true
    cancelRaf()
    stopZXing()
    setShowHelp(false)
    setStatus('Code détecté, traitement en cours…')
    try {
      await Promise.resolve(onDetected(value))
      setStatus('Visez le code‑barres')
    } catch {
      dispatchError('Traitement du code détecté impossible. Réessayez.')
    } finally {
      processingRef.current = false
      if (activeRef.current && restartRef.current) {
        try {
          await restartRef.current()
          if (activeRef.current) scheduleHelp()
        } catch {
          if (source === 'zxing') {
            dispatchError('Le lecteur s’est arrêté de manière inattendue. Réactivez la caméra.')
          }
        }
      }
    }
  }, [cancelRaf, dispatchError, onDetected, scheduleHelp, stopZXing])

  const startBarcodeDetector = useCallback(async () => {
    const el = videoRef.current
    const BarcodeDetectorAny = (window as any).BarcodeDetector
    if (!el || typeof BarcodeDetectorAny !== 'function') return false
    let detector: BarcodeDetector
    try {
      detector = new BarcodeDetectorAny({ formats: formats.map((f) => ( {
        EAN_13:'ean_13', EAN_8:'ean_8', CODE_128:'code_128', CODE_39:'code_39', ITF:'itf', QR_CODE:'qr_code'
      } as Record<SupportedFormat,DetectorFormat>)[f] ) })
    } catch { return false }

    const canvas = document.createElement('canvas')
    const ctx = canvas.getContext('2d', { willReadFrequently: true })
    if (!ctx) return false

    const loop = async () => {
      if (!activeRef.current) return
      const now = performance.now()
      // throttling ~12 fps pour limiter la charge sur iPhone
      if (now - lastLoopRef.current < 80) {
        rafRef.current = requestAnimationFrame(loop); return
      }
      lastLoopRef.current = now

      const ready = el.readyState >= HTMLMediaElement.HAVE_CURRENT_DATA
      if (ready) {
        const { videoWidth, videoHeight } = el
        if (videoWidth && videoHeight) {
          const cropW = Math.floor(videoWidth * 0.75)
          const cropH = Math.floor(videoHeight * 0.5)
          const cropX = Math.floor((videoWidth - cropW) / 2)
          const cropY = Math.floor((videoHeight - cropH) / 2)
          if (canvas.width !== cropW || canvas.height !== cropH) {
            canvas.width = cropW; canvas.height = cropH
          }
          ctx.drawImage(el, cropX, cropY, cropW, cropH, 0, 0, canvas.width, canvas.height)
          try {
            const res = await detector.detect(canvas)
            const match = res.find((r) => r.rawValue)
            if (match?.rawValue) {
              await handleDetectedValue(match.rawValue, 'detector')
              if (!activeRef.current) return
            }
          } catch { /* ignore */ }
        }
      }
      rafRef.current = requestAnimationFrame(loop)
    }
    restartRef.current = async () => { if (activeRef.current) await startBarcodeDetector() }
    rafRef.current = requestAnimationFrame(loop)
    return true
  }, [formats, handleDetectedValue, videoRef])

  const startZXing = useCallback(async () => {
    const el = videoRef.current
   ;(readerRef.current = new BrowserMultiFormatReader(hints))
    try {
      const controls = await readerRef.current.decodeFromVideoElement(el!, async (result, err) => {
        if (processingRef.current) return
        if (result) { await handleDetectedValue(result.getText(), 'zxing'); return }
        if (!err || err instanceof NotFoundException) return
        const now = Date.now()
        if (now - lastZXLogRef.current > 2000) { lastZXLogRef.current = now }
      })
      controlsRef.current = controls
      restartRef.current = async () => { if (activeRef.current) await startZXing() }
    } catch {
      dispatchError('Lecture du flux caméra impossible. Vérifiez les autorisations.')
    }
  }, [dispatchError, handleDetectedValue, hints, videoRef])

  // Démarrage/arrêt de la détection
  useEffect(() => {
    activeRef.current = active && (usingExternalCamera ? externalActive : isSecureCameraAvailable())
    if (!activeRef.current) {
      cleanup()
      setStatus('')
      setError(null)
      setShowHelp(false)
      setTorchSupported(false)
      setTorchEnabled(false)
      return
    }

    // Si on nous fournit un flux externe, on ne le touche pas : on ne fait que détecter.
    const el = videoRef.current
    if (!el) return

    setStatus('Initialisation caméra…')
    setError(null)
    setShowHelp(false)

    const stream = (el as any).srcObject as MediaStream | null
    const [track] = stream?.getVideoTracks?.() ?? []
    const caps = track?.getCapabilities?.() as TorchCapabilities | undefined
    setTorchSupported(Boolean(caps?.torch))
    setTorchEnabled(false)
    setStatus('Visez le code‑barres')

    let cancelled = false
    ;(async () => {
      try {
        const ok = await startBarcodeDetector()
        if (!ok) await startZXing()
        if (!cancelled) scheduleHelp()
      } catch {
        dispatchError('Initialisation du lecteur impossible.')
      }
    })()

    const onVis = () => {
      if (document.visibilityState === 'hidden') {
        cleanup()
      } else if (activeRef.current && restartRef.current) {
        void restartRef.current()
      }
    }
    document.addEventListener('visibilitychange', onVis)

    return () => {
      cancelled = true
      document.removeEventListener('visibilitychange', onVis)
      cleanup()
    }
  }, [active, cleanup, dispatchError, externalActive, scheduleHelp, startBarcodeDetector, startZXing, usingExternalCamera, videoRef])

  const handleImageChange = (event: ChangeEvent<HTMLInputElement>) => {
    if (!onPickImage) return
    const [file] = Array.from(event.target.files ?? [])
    if (file) onPickImage(file)
    event.target.value = ''
  }

  const containerClass = presentation === 'immersive'
    ? 'relative h-full w-full bg-black text-slate-100'
    : 'relative overflow-hidden rounded-3xl border border-slate-700 bg-black/80 text-slate-100'

  const videoClass = presentation === 'immersive'
    ? 'h-full w-full object-cover will-change-transform'
    : 'h-64 w-full object-contain bg-black will-change-transform'

  return (
    <div className={containerClass}>
      <div className={presentation === 'immersive' ? 'relative h-full w-full' : 'flex flex-col gap-3'}>
        <div className="relative h-full w-full">
          <video ref={videoRef} className={videoClass} muted playsInline autoPlay />
          <div
            className={clsx(
              'pointer-events-none absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 rounded-2xl border-4 border-brand-400/70',
              presentation === 'immersive' ? 'h-1/2 w-3/4 max-w-md' : 'h-32 w-64',
            )}
            aria-hidden
          />
        </div>
        {presentation !== 'immersive' && (
          <div className="space-y-2 px-4 pb-4">
            {status && <p className="text-sm text-brand-200">{status}</p>}
            {error && <p className="text-sm text-red-300">{error}</p>}
            {!error && !status && active && <p className="text-sm text-slate-200">Visez le code‑barres.</p>}
            {showHelp && (
              <p className="text-xs text-amber-200">
                Aucune détection pour l’instant. Approchez le code, ajustez l’éclairage
                ou importez une photo.
              </p>
            )}
            {enableTorchToggle && (
              <button
                type="button"
                className="rounded-xl border border-slate-500 px-3 py-2 text-sm font-semibold text-slate-100 transition hover:border-brand-400 disabled:cursor-not-allowed disabled:border-slate-700 disabled:text-slate-500"
                onClick={() => void setTorch(!torchEnabled)}
                disabled={!torchSupported}
              >
                {torchSupported ? (torchEnabled ? 'Éteindre la lampe' : 'Allumer la lampe') : 'Lampe non prise en charge'}
              </button>
            )}
            {showHelp && onPickImage && (
              <label className="flex cursor-pointer flex-col gap-2 rounded-2xl border border-slate-500 bg-slate-800/40 p-3 text-sm text-slate-100 hover:border-brand-400">
                <span className="font-medium">Importer une photo du code‑barres</span>
                <input type="file" accept="image/*" capture="environment" className="text-xs text-slate-300" onChange={handleImageChange} />
              </label>
            )}
          </div>
        )}
        {presentation === 'immersive' && status && (
          <div className="pointer-events-none absolute inset-x-0 bottom-6 flex justify-center">
            <span className="rounded-full bg-black/60 px-3 py-1 text-xs font-semibold text-emerald-200 backdrop-blur">
              {status}
            </span>
          </div>
        )}
      </div>
    </div>
  )
}
