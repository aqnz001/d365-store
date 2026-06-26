import { useState } from 'react'
import { startCheckout, pay, type CheckoutResult, type PayResult } from '../api'

export function Checkout() {
  const [checkout, setCheckout] = useState<CheckoutResult>()
  const [order, setOrder] = useState<PayResult>()
  const [error, setError] = useState<string>()

  const start = async () => {
    try {
      setCheckout(await startCheckout())
    } catch (e) {
      setError((e as Error).message)
    }
  }

  const placeOrder = async () => {
    if (!checkout) return
    try {
      // Card is collected by the provider's hosted Payment Element (SAQ-A); the BFF authorizes
      // against an opaque token — "ok" exercises the Phase-1 fake provider.
      setOrder(await pay({ amount: 0, currency: 'GBP', paymentToken: 'ok', reservationIds: checkout.reservationIds }))
    } catch (e) {
      setError((e as Error).message)
    }
  }

  return (
    <section>
      <h1>Checkout</h1>
      <button onClick={start}>Start checkout</button>
      {error && <p style={{ color: 'crimson' }}>{error}</p>}
      {checkout && (
        <p>
          Status: <strong>{checkout.status}</strong> {checkout.message}
        </p>
      )}
      {checkout?.status === 'Ready' && !order && <button onClick={placeOrder}>Pay &amp; place order</button>}
      {order && (
        <p>
          Order: <strong>{order.status}</strong> {order.orderReference}
        </p>
      )}
    </section>
  )
}
