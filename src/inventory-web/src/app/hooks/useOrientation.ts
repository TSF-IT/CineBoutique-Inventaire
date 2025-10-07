import { useEffect, useState } from 'react'

export type Orientation = 'portrait' | 'landscape'

const getOrientation = (): Orientation => {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
    return 'portrait'
  }

  return window.matchMedia('(orientation: portrait)').matches ? 'portrait' : 'landscape'
}

export const useOrientation = (): Orientation => {
  const [orientation, setOrientation] = useState<Orientation>(getOrientation)

  useEffect(() => {
    if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
      return
    }

    const query = window.matchMedia('(orientation: portrait)')
    const handleChange = () => {
      setOrientation(query.matches ? 'portrait' : 'landscape')
    }

    handleChange()

    if (typeof query.addEventListener === 'function') {
      query.addEventListener('change', handleChange)
      return () => query.removeEventListener('change', handleChange)
    }

    query.addListener(handleChange)
    return () => query.removeListener(handleChange)
  }, [])

  return orientation
}
