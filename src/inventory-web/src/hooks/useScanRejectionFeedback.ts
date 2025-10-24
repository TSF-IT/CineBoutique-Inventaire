import { useCallback, useEffect, useRef } from 'react'

const FLASH_DURATION_MS = 900

const removeFlashClass = () => {
  if (typeof document === 'undefined') {
    return
  }
  document.body.classList.remove('inventory-flash-active')
}

export const useScanRejectionFeedback = () => {
  const flashTimeoutRef = useRef<number | null>(null)
  const audioContextRef = useRef<AudioContext | null>(null)

  useEffect(() => {
    return () => {
      if (flashTimeoutRef.current !== null) {
        window.clearTimeout(flashTimeoutRef.current)
        flashTimeoutRef.current = null
      }
      removeFlashClass()
      const context = audioContextRef.current
      audioContextRef.current = null
      if (context && context.state !== 'closed') {
        void context.close().catch(() => undefined)
      }
    }
  }, [])

  const triggerFlash = useCallback(() => {
    if (typeof document === 'undefined') {
      return
    }
    const target = document.body
    if (!target) {
      return
    }
    target.classList.add('inventory-flash-active')
    if (flashTimeoutRef.current !== null) {
      window.clearTimeout(flashTimeoutRef.current)
    }
    flashTimeoutRef.current = window.setTimeout(() => {
      removeFlashClass()
      flashTimeoutRef.current = null
    }, FLASH_DURATION_MS)
  }, [])

  const playNegativeTone = useCallback(() => {
    if (typeof window === 'undefined') {
      return
    }
    const AudioContextCtor =
      window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext
    if (!AudioContextCtor) {
      return
    }

    if (!audioContextRef.current) {
      audioContextRef.current = new AudioContextCtor()
    }

    const context = audioContextRef.current
    if (!context) {
      return
    }

    if (context.state === 'suspended') {
      void context.resume().catch(() => undefined)
    }

    const now = context.currentTime
    const oscillator = context.createOscillator()
    const gain = context.createGain()

    oscillator.type = 'sawtooth'
    oscillator.frequency.setValueAtTime(380, now)
    oscillator.frequency.exponentialRampToValueAtTime(190, now + 0.28)

    gain.gain.setValueAtTime(0.0001, now)
    gain.gain.exponentialRampToValueAtTime(0.2, now + 0.02)
    gain.gain.exponentialRampToValueAtTime(0.0001, now + 0.4)

    oscillator.connect(gain)
    gain.connect(context.destination)

    oscillator.start(now)
    oscillator.stop(now + 0.42)
    oscillator.onended = () => {
      oscillator.disconnect()
      gain.disconnect()
    }
  }, [])

  return useCallback(() => {
    triggerFlash()
    playNegativeTone()
  }, [playNegativeTone, triggerFlash])
}
