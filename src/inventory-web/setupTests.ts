import '@testing-library/jest-dom/vitest'
import { afterEach, vi } from 'vitest'
import { cleanup } from '@testing-library/react'

afterEach(() => {
  cleanup()
})

if (typeof window !== 'undefined') {
  window.confirm = vi.fn(() => true)

  if (typeof window.requestAnimationFrame !== 'function') {
    window.requestAnimationFrame = (callback: FrameRequestCallback) => {
      return window.setTimeout(() => callback(performance.now()), 16)
    }
  }

  if (typeof window.cancelAnimationFrame !== 'function') {
    window.cancelAnimationFrame = (handle: number) => {
      window.clearTimeout(handle)
    }
  }
}
