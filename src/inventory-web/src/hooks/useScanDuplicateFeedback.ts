import { useCallback, useEffect, useRef } from 'react'

const FLASH_CLASS = 'inventory-duplicate-flash-active'
const FLASH_DURATION_MS = 900

const removeFlashClass = () => {
  if (typeof document === 'undefined') {
    return
  }
  document.body.classList.remove(FLASH_CLASS)
}

export const useScanDuplicateFeedback = () => {
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
    target.classList.add(FLASH_CLASS)
    if (flashTimeoutRef.current !== null) {
      window.clearTimeout(flashTimeoutRef.current)
    }
    flashTimeoutRef.current = window.setTimeout(() => {
      removeFlashClass()
      flashTimeoutRef.current = null
    }, FLASH_DURATION_MS)
  }, [])

  const playPositiveTone = useCallback(() => {
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

    oscillator.type = 'triangle'
    oscillator.frequency.setValueAtTime(640, now)
    oscillator.frequency.linearRampToValueAtTime(860, now + 0.14)
    oscillator.frequency.exponentialRampToValueAtTime(520, now + 0.36)

    gain.gain.setValueAtTime(0.0001, now)
    gain.gain.exponentialRampToValueAtTime(0.18, now + 0.02)
    gain.gain.exponentialRampToValueAtTime(0.0001, now + 0.42)

    oscillator.connect(gain)
    gain.connect(context.destination)

    oscillator.start(now)
    oscillator.stop(now + 0.45)
    oscillator.onended = () => {
      oscillator.disconnect()
      gain.disconnect()
    }
  }, [])

  return useCallback(() => {
    triggerFlash()
    playPositiveTone()
  }, [playPositiveTone, triggerFlash])
}
