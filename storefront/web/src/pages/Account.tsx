import { useEffect, useState } from 'react'
import { getOrders, getCredit, type PlacedOrder, type CreditStanding } from '../api'

export function Account() {
  const [orders, setOrders] = useState<PlacedOrder[]>([])
  const [credit, setCredit] = useState<CreditStanding>()

  useEffect(() => {
    getOrders()
      .then(setOrders)
      .catch(() => undefined)
    getCredit()
      .then(setCredit)
      .catch(() => undefined)
  }, [])

  return (
    <section>
      <h1>Account</h1>
      {credit && (
        <p>
          Credit: <strong>{credit.creditStatus}</strong> ({credit.decision})
        </p>
      )}
      <h2>Orders</h2>
      {orders.length === 0 ? (
        <p>No orders yet.</p>
      ) : (
        <ul>
          {orders.map((order) => (
            <li key={order.orderReference}>
              {order.orderReference} — {new Date(order.placedAtUtc).toLocaleString()}
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
