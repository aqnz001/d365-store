// Shared cart state so the header badge stays in sync with add/remove from any page.
// The BFF is the source of truth (server-side cart, DR-002); this just mirrors it for the UI.
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { getCart, type ShoppingCart } from '../api'

interface CartContextValue {
  cart?: ShoppingCart
  count: number
  loading: boolean
  refresh: () => Promise<void>
  applyCart: (cart: ShoppingCart) => void
}

const CartContext = createContext<CartContextValue | undefined>(undefined)

function itemCount(cart?: ShoppingCart): number {
  return cart?.lines.reduce((sum, line) => sum + (line.quantity || 0), 0) ?? 0
}

export function CartProvider({ children }: { children: ReactNode }) {
  const [cart, setCart] = useState<ShoppingCart>()
  const [loading, setLoading] = useState(true)

  const refresh = useCallback(async () => {
    setLoading(true)
    try {
      setCart(await getCart())
    } catch {
      // Leave the last-known cart in place on a transient failure; the page surfaces errors.
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void refresh()
  }, [refresh])

  const value = useMemo<CartContextValue>(
    () => ({ cart, count: itemCount(cart), loading, refresh, applyCart: setCart }),
    [cart, loading, refresh],
  )

  return <CartContext.Provider value={value}>{children}</CartContext.Provider>
}

export function useCart(): CartContextValue {
  const context = useContext(CartContext)
  if (!context) {
    throw new Error('useCart must be used within a CartProvider')
  }
  return context
}
