import {
  createContext,
  type ReactNode,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
} from 'react'
import type { Shop } from '@/types/shop'
import { clearShop, loadShop, saveShop } from '@/lib/shopStorage'

type ShopState = {
  shop: Shop | null
  setShop: (shop: Shop | null) => void
  isLoaded: boolean
}

const ShopContext = createContext<ShopState | null>(null)

export const ShopProvider = ({ children }: { children: ReactNode }) => {
  const [shop, setShopState] = useState<Shop | null>(null)
  const [isLoaded, setIsLoaded] = useState(false)

  useEffect(() => {
    setShopState(loadShop())
    setIsLoaded(true)
  }, [])

  const setShop = useCallback((nextShop: Shop | null) => {
    if (nextShop) {
      saveShop(nextShop)
    } else {
      clearShop()
    }
    setShopState(nextShop)
  }, [])

  const value = useMemo(
    () => ({
      shop,
      setShop,
      isLoaded,
    }),
    [shop, isLoaded, setShop],
  )

  return <ShopContext.Provider value={value}>{children}</ShopContext.Provider>
}

export const useShop = () => {
  const context = useContext(ShopContext)
  if (!context) {
    throw new Error('useShop must be used within ShopProvider')
  }

  return context
}
