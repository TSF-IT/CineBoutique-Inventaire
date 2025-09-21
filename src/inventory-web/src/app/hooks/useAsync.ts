import { useCallback, useEffect, useState } from 'react'

interface UseAsyncOptions<T> {
  immediate?: boolean
  onError?: (error: unknown) => void
  initialValue?: T | null
}

export const useAsync = <T,>(asyncFn: () => Promise<T>, deps: unknown[], options?: UseAsyncOptions<T>) => {
  const { immediate = true, onError, initialValue = null } = options ?? {}
  const [data, setData] = useState<T | null>(initialValue)
  const [loading, setLoading] = useState(immediate)
  const [error, setError] = useState<unknown>(null)

  const execute = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const result = await asyncFn()
      setData(result)
      return result
    } catch (err) {
      setError(err)
      onError?.(err)
      throw err
    } finally {
      setLoading(false)
    }
  }, deps)

  useEffect(() => {
    if (!immediate) {
      return
    }
    void execute()
  }, [execute, immediate])

  return { data, loading, error, execute, setData }
}
