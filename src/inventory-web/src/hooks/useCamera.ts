import { useCallback, useEffect, useRef, useState, type MutableRefObject } from 'react'

export type CameraOptions = {
  constraints?: MediaStreamConstraints
  /** Si true, coupe la caméra quand l’onglet est caché et la relance quand il redevient visible */
  autoResumeOnVisible?: boolean
}

type StreamEnabledVideo = HTMLVideoElement & { srcObject: MediaStream | null }
const setVideoStream = (video: HTMLVideoElement, stream: MediaStream | null) => {
  (video as StreamEnabledVideo).srcObject = stream
}

export function useCamera(
  videoRef: MutableRefObject<HTMLVideoElement | null>,
  {
    constraints = { video: { facingMode: { ideal: 'environment' } }, audio: false },
    autoResumeOnVisible = true,
  }: CameraOptions = {},
) {
  const [active, setActive] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const startingRef = useRef(false)

  const stop = useCallback(() => {
    const el = videoRef.current
    const stream = el ? (el as StreamEnabledVideo).srcObject : null
    if (stream) {
      for (const track of stream.getTracks()) {
        if (typeof track.stop === 'function') track.stop()
      }
    }
    if (el) {
      el.pause()
      setVideoStream(el, null)
      el.removeAttribute('src')
    }
    setActive(false)
  }, [videoRef])

  const start = useCallback(async () => {
    if (startingRef.current) return
    const el = videoRef.current
    if (!el) return
    startingRef.current = true
    setError(null)
    try {
      const stream = await navigator.mediaDevices.getUserMedia(constraints)
      el.setAttribute('playsinline', 'true') // iOS
      el.muted = true
      setVideoStream(el, stream)
      await el.play().catch(() => undefined)
      setActive(true)
    } catch (err) {
      setError(err)
      setActive(false)
    } finally {
      startingRef.current = false
    }
  }, [constraints, videoRef])

  // Lance/arrête quand la balise <video> est disponible / quand le composant monte-démonte
  useEffect(() => {
    const el = videoRef.current
    if (!el) return
    let cancelled = false
    ;(async () => { if (!cancelled) await start() })()
    return () => { cancelled = true; stop() }
  }, [start, stop, videoRef])

  // Couper/reprendre sur visibilitychange (utile sur iOS/PWA)
  useEffect(() => {
    if (!autoResumeOnVisible) return
    const onVis = () => {
      if (document.visibilityState === 'hidden') {
        stop()
      } else {
        // si le flux est déjà actif, ne rien faire
        if (!active) void start()
      }
    }
    document.addEventListener('visibilitychange', onVis)
    return () => document.removeEventListener('visibilitychange', onVis)
  }, [active, autoResumeOnVisible, start, stop])

  return { active, error, start, stop }
}
