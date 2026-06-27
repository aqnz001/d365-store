import { useEffect, useState } from 'react'
import { getOrders, getCredit, type PlacedOrder, type CreditStanding } from '../api'
import { Spinner, Banner, EmptyState, CreditBadge } from '../components/ui'

export function Account() {
  const [orders, setOrders] = useState<PlacedOrder[]>()
  const [credit, setCredit] = useState<CreditStanding>()
  const [error, setError] = useState<string>()

  useEffect(() => {
    getOrders()
      .then(setOrders)
      .catch((e: Error) => setError(e.message))
    getCredit()
      .then(setCredit)
      .catch(() => undefined)
  }, [])

  return (
    <section>
      <div className="page-head">
        <h1>Account</h1>
      </div>
      {error && <Banner kind="danger">{error}</Banner>}
      {credit && (
        <div className="summary" style={{ marginBottom: 20 }}>
          <div className="row">
            <span>Customer</span>
            <strong>{credit.customerAccount}</strong>
          </div>
          <div className="row">
            <span>Credit / net terms</span>
            <CreditBadge decision={credit.decision} label={credit.creditStatus} />
          </div>
        </div>
      )}

      <h2>Order history</h2>
      {!orders && !error && (
        <p className="muted">
          <Spinner /> Loading…
        </p>
      )}
      {orders && orders.length === 0 && <EmptyState>No orders yet.</EmptyState>}
      {orders && orders.length > 0 && (
        <div className="cart-list">
          {orders.map((order) => (
            <div className="cart-row" key={order.orderReference}>
              <div className="grow">
                <strong>{order.orderReference}</strong>
                <div className="muted" style={{ fontSize: 13 }}>{new Date(order.placedAtUtc).toLocaleString()}</div>
              </div>
              <span className="badge badge-muted">Queued</span>
            </div>
          ))}
        </div>
      )}
    </section>
  )
}
