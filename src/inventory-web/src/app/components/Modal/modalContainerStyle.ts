import type { CSSProperties } from 'react'

const modalViewportGap = 'var(--modal-viewport-gap, 3rem)'

export const modalContainerStyle: CSSProperties = {
  maxHeight: `min(calc(100vh - ${modalViewportGap}), calc(100dvh - ${modalViewportGap}))`,
}

