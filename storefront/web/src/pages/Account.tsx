import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { getOrders, getCredit, getOrderStatus, type PlacedOrder, type CreditStanding, type OrderStatus } from '../api'
import { Badge, Banner, CreditBadge, EmptyState, Eyebrow, Loading } from '../components/ui'
import { useCurrentUser } from '../context/auth'
import { formatMoney } from '../format'
import { AddressBook } from '../components/AddressBook'

const orderStatusTone: Record<string, string> = {
  Fulfilled: 'ok', PartiallyFulfilled: 'warn', WrittenBack: 'info', Queued: 'info', Accepted: 'muted',
  Rejected: 'danger', OnCreditHold: 'warn', Cancelled: 'danger', Returned: 'warn',
}
const orderStatusLabel: Record<string, string> = {
  Fulfilled: 'Shipped', PartiallyFulfilled: 'Partially shipped', WrittenBack: 'Processing', Queued: 'Queued', Accepted: 'Received',
  Rejected: 'Rejected', OnCreditHold: 'On credit hold', Cancelled: 'Cancelled', Returned: 'Returned',
}

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
  const user = useCurrentUser()
  const [orders, setOrders] = useState<PlacedOrder[]>()
  const [credit, setCredit] = useState<CreditStanding>()
  const [statuses, setStatuses] = useState<Record<string, OrderStatus | null>>({})
  const [error, setError] = useState<string>()

  useEffect(() => {
    getOrders()
      .then(async (placed) => {
        setOrders(placed)
        // Fetch the live status for each order (null = no status event yet → "Queued").
        const entries = await Promise.all(
          placed.map(async (o) => [o.orderReference, await getOrderStatus(o.orderReference).catch(() => null)] as const),
        )
        setStatuses(Object.fromEntries(entries))
      })
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

      {/* Profile — read-only identity from the session; editing is delegated to Microsoft Entra. */}
      <div className="panel panel-pad" style={{ marginBottom: 28 }}>
        <Eyebrow>Profile</Eyebrow>
        <h2 style={{ margin: '6px 0 10px' }}>{user?.name ?? user?.customerAccount ?? 'Your profile'}</h2>
        <div className="profile-grid">
          {user?.email && (
            <div>
              <div className="profile-k">Email</div>
              <div className="profile-v">{user.email}</div>
            </div>
          )}
          <div>
            <div className="profile-k">Account</div>
            <div className="profile-v mono">{user?.customerAccount ?? '—'}</div>
          </div>
        </div>
        <p className="muted" style={{ fontSize: 12, marginTop: 12 }}>
          Name, email, and password are managed in your Microsoft sign-in profile.
        </p>
      </div>

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
          {credit.creditLimit != null && (
            <div className="credit-meter">
              <div className="credit-figs">
                <div>
                  <div className="profile-k">Credit limit</div>
                  <div className="credit-amt tnum">{formatMoney(credit.creditLimit)}</div>
                </div>
                <div>
                  <div className="profile-k">Available</div>
                  <div className="credit-amt tnum">{formatMoney(credit.availableCredit ?? credit.creditLimit)}</div>
                </div>
              </div>
              {credit.availableCredit != null && credit.creditLimit > 0 && (
                <div
                  className="credit-bar"
                  role="meter"
                  aria-label="Credit used"
                  aria-valuemin={0}
                  aria-valuemax={credit.creditLimit}
                  aria-valuenow={Math.max(0, credit.creditLimit - credit.availableCredit)}
                >
                  <span style={{ width: `${Math.min(100, Math.max(0, (1 - credit.availableCredit / credit.creditLimit) * 100))}%` }} />
                </div>
              )}
            </div>
          )}
        </div>
      )}

      <AddressBook />

      <Eyebrow>Order history</Eyebrow>
      <h2 style={{ fontSize: 20, margin: '6px 0 16px' }}>Recent orders</h2>
      {/* The status badges start at "Queued" and update once the live status resolves; announce that politely. */}
      <span className="sr-only" role="status" aria-live="polite">
        {Object.keys(statuses).length > 0 ? 'Live order statuses updated.' : ''}
      </span>

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
              {orders.map((order) => {
                const live = statuses[order.orderReference]
                const code = live?.status ?? 'Queued'
                return (
                  <tr key={order.orderReference}>
                    <td className="ref">
                      <Link to={`/account/orders/${encodeURIComponent(order.orderReference)}`}>{order.orderReference}</Link>
                    </td>
                    <td className="muted tnum">{new Date(order.placedAtUtc).toLocaleString()}</td>
                    <td style={{ textAlign: 'right' }}>
                      <Badge tone={orderStatusTone[code] ?? 'muted'}>{orderStatusLabel[code] ?? code}</Badge>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
