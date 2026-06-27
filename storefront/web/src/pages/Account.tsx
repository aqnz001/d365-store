import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { getOrders, getCredit, type PlacedOrder, type CreditStanding } from '../api'
import { Badge, Banner, CreditBadge, EmptyState, Eyebrow, Loading } from '../components/ui'

const netTerms: Record<string, string> = {
  Approved: 'Approved for net terms — orders proceed without a credit hold.',
  RequiresApproval: 'Near your credit limit — larger orders may route to approval.',
  Blocked: 'Account is on credit hold — contact your account manager to release it.',
}

const creditPillTone: Record<string, string> = {
  Approved: 'ok',
  RequiresApproval: 'warn',
  Blocked: 'danger',
}

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
    <div className="container page">
      <div className="page-head">
        <Eyebrow accent>Your account</Eyebrow>
        <h1>Account</h1>
      </div>
      {error && <Banner kind="danger">{error}</Banner>}

      {credit && (
        <div className="panel panel-pad" style={{ marginBottom: 28 }}>
          <div className="credit-panel">
            <div>
              <Eyebrow>Account standing</Eyebrow>
              <h2 style={{ margin: '6px 0 2px' }}>Trade account</h2>
              <div className="mono" style={{ color: 'var(--ink-2)', fontSize: 13 }}>{credit.customerAccount}</div>
              <span className={`credit-pill badge-${creditPillTone[credit.decision] ?? 'muted'}`}>
                {credit.creditStatus}
              </span>
            </div>
            <div className="vr" />
            <div>
              <Eyebrow>Net terms</Eyebrow>
              <p style={{ margin: '8px 0 0', maxWidth: '40ch' }}>{netTerms[credit.decision] ?? 'Net terms by arrangement.'}</p>
              <div style={{ marginTop: 12 }}>
                <CreditBadge decision={credit.decision} />
              </div>
            </div>
          </div>
        </div>
      )}

      <Eyebrow>Order history</Eyebrow>
      <h2 style={{ fontSize: 20, margin: '6px 0 16px' }}>Recent orders</h2>

      {!orders && !error && <Loading>Loading orders…</Loading>}
      {orders && orders.length === 0 && (
        <EmptyState title="No orders yet">
          When you place an order it appears here. <Link to="/">Browse the catalog</Link>.
        </EmptyState>
      )}
      {orders && orders.length > 0 && (
        <div style={{ overflowX: 'auto' }}>
          <table className="ledger">
            <thead>
              <tr>
                <th>Order reference</th>
                <th>Placed</th>
                <th style={{ textAlign: 'right' }}>Status</th>
              </tr>
            </thead>
            <tbody>
              {orders.map((order) => (
                <tr key={order.orderReference}>
                  <td className="ref">{order.orderReference}</td>
                  <td className="muted tnum">{new Date(order.placedAtUtc).toLocaleString()}</td>
                  <td style={{ textAlign: 'right' }}>
                    <Badge tone="info">Queued</Badge>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
