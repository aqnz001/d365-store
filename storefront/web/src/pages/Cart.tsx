import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { getCart, type ShoppingCart } from '../api'

export function Cart() {
  const [cart, setCart] = useState<ShoppingCart>({ lines: [] })

  useEffect(() => {
    getCart()
      .then(setCart)
      .catch(() => undefined)
  }, [])

  return (
    <section>
      <h1>Cart</h1>
      {cart.lines.length === 0 ? (
        <p>Your cart is empty.</p>
      ) : (
        <ul>
          {cart.lines.map((line, index) => (
            <li key={`${line.itemNumber}-${index}`}>
              {line.itemNumber} × {line.quantity} (site {line.site})
            </li>
          ))}
        </ul>
      )}
      <Link to="/checkout">Proceed to checkout</Link>
    </section>
  )
}
