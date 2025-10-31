import React, { useCallback, useMemo, useSyncExternalStore } from 'react'

type UpdateNotifier = {
  show: () => void
  hide: () => void
  onAccept: (cb: () => void) => void
}

export const createUpdateToast = (): [React.FC, UpdateNotifier] => {
  let acceptCallback: () => void = () => {}
  let visibleState = false
  const listeners = new Set<() => void>()

  const subscribe = (listener: () => void) => {
    listeners.add(listener)
    return () => {
      listeners.delete(listener)
    }
  }

  const emit = () => {
    listeners.forEach((listener) => listener())
  }

  const applyVisibility = (next: boolean) => {
    if (visibleState === next) {
      return
    }

    visibleState = next
    emit()
  }

  const show = () => applyVisibility(true)
  const hide = () => applyVisibility(false)
  const onAccept = (cb: () => void) => {
    acceptCallback = cb
  }

  const getSnapshot = () => visibleState

  const UpdateToast: React.FC = () => {
    const visible = useSyncExternalStore(subscribe, getSnapshot, () => false)

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
              Mettre \u00E0 jour
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
