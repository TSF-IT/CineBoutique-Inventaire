import type { ReactNode } from 'react'
import { useEffect, useMemo } from 'react'
import { createPortal } from 'react-dom'

interface ModalPortalProps {
  children: ReactNode
}

export const ModalPortal = ({ children }: ModalPortalProps) => {
  const container = useMemo(() => {
    if (typeof document === 'undefined') {
      return null
    }

    const element = document.createElement('div')
    element.setAttribute('data-modal-portal', 'true')
    element.style.position = 'relative'
    return element
  }, [])

  useEffect(() => {
    if (!container || typeof document === 'undefined') {
      return
    }

    document.body.appendChild(container)
    return () => {
      document.body.removeChild(container)
    }
  }, [container])

  if (!container) {
    return null
  }

  return createPortal(children, container)
}
