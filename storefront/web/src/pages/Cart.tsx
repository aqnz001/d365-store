import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { getCart, removeFromCart, validateCart, type ShoppingCart, type CartValidateResponse } from '../api'
import { Spinner, Banner, EmptyState, BandBadge } from '../components/ui'

export function Cart() {
  const [cart, setCart] = useState<ShoppingCart>()
  const [validation, setValidation] = useState<CartValidateResponse>()
  const [validating, setValidating] = useState(false)
  const [error, setError] = useState<string>()

  useEffect(() => {
    getCart()
      .then(setCart)
      .catch((e: Error) => setError(e.message))
  }, [])

  const remove = async (index: number) => {
    setCart(await removeFromCart(index))
    setValidation(undefined)
  }

  const validate = async () => {
    setValidating(true)
    try {
      setValidation(await validateCart())
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setValidating(false)
    }
  }

  const bandFor = (item: string) => validation?.lines.find((l) => l.itemNumber === item)?.band

  if (!cart && !error) {
    return (
      <p className="muted">
        <Spinner /> Loading…
      </p>
    )
  }

  return (
    <section>
      <div className="page-head">
        <h1>Your cart</h1>
      </div>
      {error && <Banner kind="danger">{error}</Banner>}
      {cart && cart.lines.length === 0 && (
        <EmptyState>
          Your cart is empty. <Link to="/">Browse the catalog</Link>.
        </EmptyState>
      )}
      {cart && cart.lines.length > 0 && (
        <>
          <div className="cart-list">
            {cart.lines.map((line, index) => {
              const band = bandFor(line.itemNumber)
              return (
                <div className="cart-row" key={`${line.itemNumber}-${index}`}>
                  <div className="grow">
                    <strong>{line.itemNumber}</strong>
                    <div className="muted" style={{ fontSize: 13 }}>
                      Qty {line.quantity} · site {line.site}
                    </div>
                  </div>
                  {band && <BandBadge band={band} />}
                  <button className="btn-link" onClick={() => remove(index)}>
                    Remove
                  </button>
                </div>
              )
            })}
          </div>
          <div style={{ display: 'flex', gap: 10, marginTop: 16 }}>
            <button className="btn" onClick={validate} disabled={validating}>
              {validating ? <Spinner /> : 'Check availability'}
            </button>
            <Link className="btn btn-primary" to="/checkout" style={{ textDecoration: 'none' }}>
              Proceed to checkout
            </Link>
          </div>
        </>
      )}
    </section>
  )
}
