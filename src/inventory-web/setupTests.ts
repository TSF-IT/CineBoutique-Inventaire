import '@testing-library/jest-dom/vitest'
import { afterAll, afterEach, beforeAll, vi } from 'vitest'
import { cleanup } from '@testing-library/react'

import { server } from './tests/msw/server'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  cleanup()
})
afterAll(() => server.close())

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
