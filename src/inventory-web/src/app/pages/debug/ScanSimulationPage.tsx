import { useEffect, useMemo, useRef, useState } from 'react'
import { BarcodeScanner } from '../../components/BarcodeScanner'
import { Card } from '../../components/Card'
import { Button } from '../../components/ui/Button'

const SAMPLE_EAN = '5901234123457'
const SAMPLE_IMAGE_URL =
  'https://barcode.tec-it.com/barcode.ashx?data=5901234123457&code=EAN13&translate-esc=false&imagetype=png'

type MediaDevicesPatch = {
  restore: () => void
}

type TorchCapabilities = MediaTrackCapabilities & { torch?: boolean }

const prepareSimulatedStream = async (preview: HTMLVideoElement | null): Promise<MediaDevicesPatch> => {
  const canvas = document.createElement('canvas')
  canvas.width = 960
  canvas.height = 540
  const context = canvas.getContext('2d')
  if (!context) {
    throw new Error('Contexte canvas indisponible pour la simulation.')
  }

  const image = await new Promise<HTMLImageElement>((resolve, reject) => {
    const picture = new Image()
    picture.crossOrigin = 'anonymous'
    picture.src = SAMPLE_IMAGE_URL
    picture.onload = () => resolve(picture)
    picture.onerror = () => reject(new Error('Impossible de charger le visuel de test.'))
  })

  let animationFrame = 0
  const draw = () => {
    context.fillStyle = '#111827'
    context.fillRect(0, 0, canvas.width, canvas.height)
    const scale = Math.min(canvas.width / image.width, canvas.height / image.height)
    const targetWidth = image.width * scale
    const targetHeight = image.height * scale
    const offsetX = (canvas.width - targetWidth) / 2
    const offsetY = (canvas.height - targetHeight) / 2
    context.drawImage(image, offsetX, offsetY, targetWidth, targetHeight)
    context.fillStyle = 'rgba(234, 179, 8, 0.45)'
    const pulse = (Math.sin(Date.now() / 400) + 1) / 2
    context.fillRect(
      canvas.width * 0.15,
      canvas.height * 0.45,
      canvas.width * 0.7,
      canvas.height * 0.1 + pulse * 10,
    )
    animationFrame = requestAnimationFrame(draw)
  }
  draw()

  const stream = canvas.captureStream(24)
  const [track] = stream.getVideoTracks()
  const originalApplyConstraints = track.applyConstraints?.bind(track)
  track.applyConstraints = async () => {}
  track.getCapabilities = () => ({ torch: false } as TorchCapabilities)
  track.getSettings = () => ({ deviceId: 'simulated', width: canvas.width, height: canvas.height })
  track.stop = (() => {
    const original = track.stop.bind(track)
    return () => {
      cancelAnimationFrame(animationFrame)
      original()
    }
  })()

  if (preview) {
    preview.srcObject = stream
    void preview.play().catch(() => {})
  }

  const fallbackMediaDevices: MediaDevices = {
    getUserMedia: async () => {
      throw new Error('mediaDevices indisponible dans ce navigateur.')
    },
    getDisplayMedia: async () => {
      throw new Error('getDisplayMedia non supporté dans cette simulation.')
    },
    enumerateDevices: async () => [],
    getSupportedConstraints: () => ({}),
    ondevicechange: null,
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  }

  const mediaDevices = navigator.mediaDevices ??
    (Object.assign(navigator, {
      mediaDevices: fallbackMediaDevices,
    }).mediaDevices)
  const originalGetUserMedia = mediaDevices.getUserMedia.bind(mediaDevices)
  mediaDevices.getUserMedia = async () => stream

  return {
    restore: () => {
      cancelAnimationFrame(animationFrame)
      stream.getTracks().forEach((item) => item.stop())
      if (preview) {
        preview.pause()
        preview.srcObject = null
      }
      if (originalApplyConstraints) {
        track.applyConstraints = originalApplyConstraints
      }
      mediaDevices.getUserMedia = originalGetUserMedia
    },
  }
}

export const ScanSimulationPage = () => {
  const previewRef = useRef<HTMLVideoElement | null>(null)
  const [active, setActive] = useState(true)
  const [detected, setDetected] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [ready, setReady] = useState(false)

  useEffect(() => {
    let patch: MediaDevicesPatch | null = null
    let isMounted = true

    const setup = async () => {
      try {
        patch = await prepareSimulatedStream(previewRef.current)
        if (isMounted) {
          setReady(true)
        }
      } catch (err) {
        if (isMounted) {
          const message = err instanceof Error ? err.message : 'Simulation indisponible.'
          setError(message)
        }
      }
    }

    void setup()

    return () => {
      isMounted = false
      patch?.restore()
    }
  }, [])

  const helperText = useMemo(
    () =>
      ready
        ? 'Simulation alimentée par un flux vidéo généré à partir d’un EAN 13. Le flux remplace temporairement getUserMedia.'
        : 'Initialisation du flux vidéo simulé…',
    [ready],
  )

  return (
    <div className="mx-auto flex max-w-4xl flex-col gap-6 p-6">
      <Card className="space-y-4">
        <h1 className="text-2xl font-semibold text-slate-900 dark:text-white">Simulation scan caméra</h1>
        <p className="text-sm text-slate-600 dark:text-slate-300">{helperText}</p>
        <video
          ref={previewRef}
          muted
          playsInline
          autoPlay
          className="aspect-video w-full rounded-3xl border border-slate-300 object-contain bg-black"
        />
        <BarcodeScanner
          active={active && ready}
          onDetected={(value) => setDetected(value)}
          onError={(message) => setError(message)}
          enableTorchToggle={false}
          preferredFormats={['EAN_13', 'EAN_8', 'CODE_128']}
        />
        <div className="flex flex-wrap items-center gap-3">
          <Button variant="secondary" onClick={() => setActive((previous) => !previous)}>
            {active ? 'Mettre en pause la simulation' : 'Relancer la simulation'}
          </Button>
          {detected && (
            <span className="rounded-full bg-brand-100 px-3 py-1 text-sm font-semibold text-brand-800 dark:bg-brand-900 dark:text-brand-200">
              Dernière détection : {detected}
            </span>
          )}
          {error && <span className="text-sm text-red-600 dark:text-red-300">{error}</span>}
        </div>
        <p className="text-xs text-slate-500 dark:text-slate-400">
          Conseil : ouvrez la console pour vérifier les logs de détection et ajustez le composant scanner sans avoir besoin d’une
          vraie caméra.
        </p>
      </Card>
      <Card className="space-y-3 text-sm text-slate-600 dark:text-slate-300">
        <h2 className="text-lg font-semibold text-slate-900 dark:text-white">Fonctionnement</h2>
        <ul className="list-disc space-y-1 pl-5">
          <li>
            La vidéo ci-dessus est générée à partir d’un visuel public d’EAN-13 ({SAMPLE_EAN}) capturé via <code>canvas.captureStream</code>.
          </li>
          <li>La page remplace temporairement <code>navigator.mediaDevices.getUserMedia</code> pour injecter ce flux.</li>
          <li>Les capacités torch/focus sont neutralisées pour éviter des échecs lors des tests automatisés.</li>
          <li>
            Le composant <code>BarcodeScanner</code> est utilisé tel quel afin de reproduire un parcours réel (détection, messages, états).
          </li>
        </ul>
      </Card>
    </div>
  )
}
