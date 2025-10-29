import React, { useCallback, useEffect, useMemo, useState } from 'react'

import type { UpdateNotifier } from './setupPwa'

export const createUpdateToast = (): [React.FC, UpdateNotifier] => {
  let acceptCallback: () => void = () => {}
  let setVisibility: React.Dispatch<React.SetStateAction<boolean>> | undefined
  let pendingVisible = false

  const applyVisibility = (next: boolean) => {
    pendingVisible = next
    setVisibility?.(next)
  }

  const show = () => applyVisibility(true)
  const hide = () => applyVisibility(false)
  const onAccept = (cb: () => void) => {
    acceptCallback = cb
  }

  const UpdateToast: React.FC = () => {
    const [visible, internalSetVisible] = useState(pendingVisible)

    useEffect(() => {
      setVisibility = internalSetVisible
      internalSetVisible(pendingVisible)
      return () => {
        setVisibility = undefined
      }
    }, [])

    const handleLater = useCallback(() => {
      hide()
    }, [])

    const handleAccept = useCallback(() => {
      hide()
      acceptCallback()
    }, [])

    const containerStyle = useMemo<React.CSSProperties>(
      () => ({
        position: 'fixed',
        left: 0,
        right: 0,
        bottom: 0,
        zIndex: 10000,
        margin: '0 auto',
        maxWidth: 480,
        padding: '12px 16px',
        background: '#111',
        color: '#fff',
        borderRadius: 8,
        boxShadow: '0 2px 10px rgba(0,0,0,.4)',
      }),
      [],
    )

    const actionsStyle = useMemo<React.CSSProperties>(
      () => ({
        display: 'flex',
        gap: 8,
      }),
      [],
    )

    const buttonStyle = useMemo<React.CSSProperties>(
      () => ({
        padding: '6px 10px',
        border: 'none',
        borderRadius: 4,
        cursor: 'pointer',
        background: '#fff',
        color: '#111',
      }),
      [],
    )

    const primaryButtonStyle = useMemo<React.CSSProperties>(
      () => ({
        ...buttonStyle,
        background: '#2563eb',
        color: '#fff',
        fontWeight: 600,
      }),
      [buttonStyle],
    )

    if (!visible) {
      return null
    }

    return (
      <div style={containerStyle} role="alert" aria-live="assertive">
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 12,
            justifyContent: 'space-between',
          }}
        >
          <div>Nouvelle version disponible.</div>
          <div style={actionsStyle}>
            <button type="button" onClick={handleLater} style={buttonStyle}>
              Plus tard
            </button>
            <button type="button" onClick={handleAccept} style={primaryButtonStyle}>
              Mettre à jour
            </button>
          </div>
        </div>
      </div>
    )
  }

  const notifier: UpdateNotifier = {
    show,
    hide,
    onAccept,
  }

  return [UpdateToast, notifier]
}
