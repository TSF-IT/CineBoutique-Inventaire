import { useCallback, useEffect, useRef, useState } from 'react'

export type CameraOptions = {
  constraints?: MediaStreamConstraints
  /** Si true, tente de redémarrer la caméra quand la page redevient visible */
  autoResumeOnVisible?: boolean
}

type StreamEnabledVideo = HTMLVideoElement & { srcObject: MediaStream | null }

const setVideoStream = (video: HTMLVideoElement, stream: MediaStream | null) => {
  (video as StreamEnabledVideo).srcObject = stream
}

export function useCamera(videoEl: HTMLVideoElement | null, {
  constraints = { video: { facingMode: 'environment' }, audio: false },
  autoResumeOnVisible = true,
}: CameraOptions = {}) {
  const streamRef = useRef<MediaStream | null>(null)
  const [active, setActive] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const startingRef = useRef(false)

  const stop = useCallback(() => {
    const s = streamRef.current
    if (s) {
      s.getTracks().forEach(track => {
        try {
          track.stop()
        } catch (error) {
          if (import.meta.env.DEV) {
            console.debug('[camera] Arrêt piste impossible', error)
          }
        }
      })
      streamRef.current = null
    }
    if (videoEl) {
      try {
        videoEl.pause()
        // iOS : important de libérer la référence
        setVideoStream(videoEl, null)
        videoEl.removeAttribute('src')
      } catch (error) {
        if (import.meta.env.DEV) {
          console.debug('[camera] Impossible de libérer la vidéo', error)
        }
      }
    }
    setActive(false)
  }, [videoEl])

  const start = useCallback(async () => {
    if (!videoEl || startingRef.current) return
    startingRef.current = true
    setError(null)
    try {
      // Toujours créer un NOUVEAU stream (iOS n’aime pas recycler une track stoppée)
      const stream = await navigator.mediaDevices.getUserMedia(constraints)
      streamRef.current = stream
      setVideoStream(videoEl, stream)
      // iOS/WebView : playsinline + mute pour lecture auto
      videoEl.setAttribute('playsinline', 'true')
      videoEl.muted = true
      await videoEl.play().catch(() => {/* ignore */})
      setActive(true)
    } catch (e) {
      setError(e)
      stop()
    } finally {
      startingRef.current = false
    }
  }, [videoEl, constraints, stop])

  // Démarre au montage (quand le <video> est prêt)
  useEffect(() => {
    if (videoEl) start()
    return () => stop()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [videoEl])

  // Couper si la page passe en arrière-plan (PWA iOS)
  useEffect(() => {
    const onHidden = () => stop()
    const onPageHide = () => stop()
    const onBeforeUnload = () => stop()

    document.addEventListener('visibilitychange', onHidden, { passive: true })
    window.addEventListener('pagehide', onPageHide, { passive: true })
    window.addEventListener('beforeunload', onBeforeUnload, { passive: true })

    return () => {
      document.removeEventListener('visibilitychange', onHidden)
      window.removeEventListener('pagehide', onPageHide)
      window.removeEventListener('beforeunload', onBeforeUnload)
    }
  }, [stop])

  // Option : si la page redevient visible ET que le composant est toujours monté, on peut relancer auto
  useEffect(() => {
    if (!autoResumeOnVisible) return
    const onVisible = () => {
      if (document.visibilityState === 'visible') start()
    }
    document.addEventListener('visibilitychange', onVisible, { passive: true })
    return () => document.removeEventListener('visibilitychange', onVisible)
  }, [autoResumeOnVisible, start])

  return { active, error, start, stop }
}
