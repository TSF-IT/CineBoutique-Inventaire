import { useCallback, useEffect, useRef } from 'react'

type Tone = {
  frequency: number
  duration?: number
  gap?: number
  type?: OscillatorType
  volume?: number
}

type ScanFeedback = {
  playSuccess: () => void
  playError: () => void
}

type AudioContextConstructor = new (contextOptions?: AudioContextOptions) => AudioContext

type WindowWithWebkitAudio = typeof window & {
  webkitAudioContext?: AudioContextConstructor
}

const DEFAULT_VOLUME = 0.25
const DEFAULT_DURATION = 0.22
const DEFAULT_GAP = 0.06

const resolveAudioContextConstructor = (): AudioContextConstructor | null => {
  if (typeof window === 'undefined') {
    return null
  }

  const globalWindow = window as WindowWithWebkitAudio

  if (typeof globalWindow.AudioContext === 'function') {
    return globalWindow.AudioContext as AudioContextConstructor
  }

  if (typeof globalWindow.webkitAudioContext === 'function') {
    return globalWindow.webkitAudioContext
  }

  return null
}

const scheduleTones = (
  context: AudioContext,
  tones: Tone[],
  options?: { attack?: number; release?: number },
) => {
  const attack = options?.attack ?? 0.02
  const release = options?.release ?? 0.08

  let startTime = context.currentTime + 0.01

  for (const tone of tones) {
    const oscillator = context.createOscillator()
    const gainNode = context.createGain()

    oscillator.type = tone.type ?? 'sine'
    oscillator.frequency.setValueAtTime(tone.frequency, startTime)

    const duration = tone.duration ?? DEFAULT_DURATION
    const volume = tone.volume ?? DEFAULT_VOLUME

    gainNode.gain.setValueAtTime(0.0001, startTime)
    gainNode.gain.exponentialRampToValueAtTime(volume, startTime + attack)
    gainNode.gain.exponentialRampToValueAtTime(0.0001, startTime + Math.max(duration - release, attack + 0.01))

    oscillator.connect(gainNode)
    gainNode.connect(context.destination)

    oscillator.start(startTime)
    oscillator.stop(startTime + duration + release)

    startTime += duration + (tone.gap ?? DEFAULT_GAP)
  }
}

export const useScanFeedback = (): ScanFeedback => {
  const contextRef = useRef<AudioContext | null>(null)

  const ensureContext = useCallback(() => {
    if (contextRef.current) {
      return contextRef.current
    }

    const ctor = resolveAudioContextConstructor()
    if (!ctor) {
      return null
    }

    const context = new ctor()
    contextRef.current = context
    return context
  }, [])

  const playTones = useCallback(
    async (tones: Tone[]) => {
      const context = ensureContext()
      if (!context) {
        return
      }

      if (context.state === 'suspended') {
        try {
          await context.resume()
        } catch {
          return
        }
      }

      scheduleTones(context, tones)
    },
    [ensureContext],
  )

  const playSuccess = useCallback(() => {
    void playTones([
      { frequency: 880, duration: 0.18, type: 'triangle', volume: 0.28 },
      { frequency: 1320, duration: 0.22, type: 'triangle', volume: 0.24 },
    ])
  }, [playTones])

  const playError = useCallback(() => {
    void playTones([
      { frequency: 220, duration: 0.32, type: 'sawtooth', volume: 0.22 },
      { frequency: 160, duration: 0.28, type: 'sawtooth', volume: 0.18 },
    ])
  }, [playTones])

  useEffect(() => {
    return () => {
      const context = contextRef.current
      contextRef.current = null
      if (context) {
        context.close().catch(() => undefined)
      }
    }
  }, [])

  return { playSuccess, playError }
}
