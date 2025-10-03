import type { ReactNode } from 'react'
import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import { DEFAULT_OPERATORS } from '../utils/operators'

export interface OperatorDefinition {
  id: string
  name: string
}

interface OperatorsContextValue {
  operators: OperatorDefinition[]
  addOperator: (name: string) => OperatorDefinition
  updateOperator: (id: string, name: string) => OperatorDefinition
}

const STORAGE_KEY = 'cineboutique.operators'

const sanitizeOperatorName = (name: string) => name.replace(/\s+/g, ' ').trim()

const parseStoredOperators = (rawValue: string | null): OperatorDefinition[] | null => {
  if (!rawValue) {
    return null
  }

  try {
    const parsed = JSON.parse(rawValue) as unknown
    if (!Array.isArray(parsed)) {
      return null
    }

    const sanitized: OperatorDefinition[] = []
    for (const item of parsed) {
      if (typeof item !== 'object' || item === null) {
        continue
      }

      const { id, name } = item as Record<string, unknown>
      if (typeof id !== 'string' || id.trim().length === 0) {
        continue
      }

      if (typeof name !== 'string') {
        continue
      }

      const sanitizedName = sanitizeOperatorName(name)
      if (!sanitizedName) {
        continue
      }

      sanitized.push({ id, name: sanitizedName })
    }

    if (sanitized.length === 0) {
      return null
    }

    return sanitized
  } catch {
    return null
  }
}

const createOperatorId = (name: string) => {
  if (typeof globalThis.crypto !== 'undefined' && typeof globalThis.crypto.randomUUID === 'function') {
    return globalThis.crypto.randomUUID()
  }

  const slug = name
    .toLowerCase()
    .normalize('NFD')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/(^-|-$)+/g, '')

  if (slug) {
    return `operator-${slug}-${Date.now().toString(36)}`
  }

  return `operator-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`
}

const OperatorsContext = createContext<OperatorsContextValue | undefined>(undefined)

const normalizeOperators = (operators: OperatorDefinition[]) =>
  operators.map((operator) => ({ ...operator, name: sanitizeOperatorName(operator.name) })).filter((operator) => operator.name)

const hasDuplicateName = (operators: OperatorDefinition[], name: string, excludeId?: string) =>
  operators.some(
    (operator) =>
      operator.id !== excludeId && operator.name.localeCompare(name, undefined, { sensitivity: 'accent' }) === 0,
  )

export const OperatorsProvider = ({ children }: { children: ReactNode }) => {
  const [operators, setOperators] = useState<OperatorDefinition[]>(() => {
    if (typeof window === 'undefined') {
      return DEFAULT_OPERATORS
    }

    const stored = parseStoredOperators(window.localStorage.getItem(STORAGE_KEY))
    if (stored && stored.length > 0) {
      return normalizeOperators(stored)
    }

    return DEFAULT_OPERATORS
  })

  useEffect(() => {
    if (typeof window === 'undefined') {
      return
    }
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(operators))
  }, [operators])

  const addOperator = useCallback((rawName: string) => {
    const name = sanitizeOperatorName(rawName)
    if (!name) {
      throw new Error('Le nom de l’opérateur est requis.')
    }

    if (hasDuplicateName(operators, name)) {
      throw new Error('Cet opérateur existe déjà.')
    }

    const nextOperator: OperatorDefinition = { id: createOperatorId(name), name }
    setOperators((prev) => [...prev, nextOperator])
    return nextOperator
  }, [operators])

  const updateOperator = useCallback((id: string, rawName: string) => {
    const name = sanitizeOperatorName(rawName)
    if (!name) {
      throw new Error('Le nom de l’opérateur est requis.')
    }

    if (!operators.some((operator) => operator.id === id)) {
      throw new Error('Opérateur introuvable.')
    }

    if (hasDuplicateName(operators, name, id)) {
      throw new Error('Un autre opérateur porte déjà ce nom.')
    }

    setOperators((prev) => prev.map((operator) => (operator.id === id ? { ...operator, name } : operator)))
    return { id, name }
  }, [operators])

  const value = useMemo<OperatorsContextValue>(
    () => ({
      operators,
      addOperator,
      updateOperator,
    }),
    [addOperator, operators, updateOperator],
  )

  return <OperatorsContext.Provider value={value}>{children}</OperatorsContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export const useOperators = () => {
  const context = useContext(OperatorsContext)
  if (!context) {
    throw new Error('useOperators doit être utilisé avec OperatorsProvider')
  }
  return context
}
