import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { getOrderStatus, type OrderStatus } from '../api'
import { Badge, Banner, Eyebrow, Loading } from '../components/ui'
import { TruckIcon } from '../components/icons'

const statusTone: Record<string, string> = {
  Fulfilled: 'ok',
  PartiallyFulfilled: 'warn',
  WrittenBack: 'info',
  Queued: 'info',
  Accepted: 'muted',
  Rejected: 'danger',
  OnCreditHold: 'warn',
  Cancelled: 'danger',
  Returned: 'warn',
}

const statusLabel: Record<string, string> = {
  Fulfilled: 'Shipped',
  PartiallyFulfilled: 'Partially shipped',
  WrittenBack: 'Processing',
  Queued: 'Queued',
  Accepted: 'Received',
  Rejected: 'Rejected',
  OnCreditHold: 'On credit hold',
  Cancelled: 'Cancelled',
  Returned: 'Returned',
}

export function OrderDetail() {
  const { reference = '' } = useParams()
  const [status, setStatus] = useState<OrderStatus | null>()
  const [error, setError] = useState<string>()

  useEffect(() => {
    let ignore = false
    setStatus(undefined)
    setError(undefined)
    getOrderStatus(reference)
      .then((s) => {
        if (!ignore) setStatus(s)
      })
      .catch((e: Error) => {
        if (!ignore) setError(e.message)
      })
    return () => {
      ignore = true
    }
  }, [reference])

  // Persistent polite live region so the loading → resolved status transition is announced.
  const announcement = error
    ? `Could not load order ${reference}`
    : status === undefined
      ? `Loading status for order ${reference}`
      : status === null
        ? `Order ${reference}: queued for processing`
        : `Order ${reference}: ${statusLabel[status.status] ?? status.status}`

  // Terminal states have no in-flight shipment to prepare.
  const isTerminal = status != null && ['Cancelled', 'Returned', 'Rejected'].includes(status.status)

  return (
    <div className="container page" style={{ maxWidth: 820 }}>
      <span className="sr-only" role="status" aria-live="polite">{announcement}</span>
      <p style={{ marginBottom: 16 }}>
        <Link to="/account" className="muted" style={{ fontSize: 14 }}>← Back to orders</Link>
      </p>
      <div className="page-head">
        <Eyebrow accent>Order</Eyebrow>
        <h1 className="mono" style={{ fontSize: 30 }}>{reference}</h1>
      </div>

      {error && <Banner kind="danger">{error}</Banner>}
      {status === undefined && !error && <Loading>Loading order status…</Loading>}

      {status === null && (
        <div className="panel panel-pad">
          <Badge tone="info">Queued</Badge>
          <p className="muted" style={{ marginTop: 12 }}>
            Your order has been received and is queued for processing through the order writeback.
            Fulfilment and tracking details will appear here once it ships.
          </p>
        </div>
      )}

      {status && (
        <>
          <div className="panel panel-pad" style={{ marginBottom: 20 }}>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, flexWrap: 'wrap' }}>
              <Badge tone={statusTone[status.status] ?? 'muted'}>{statusLabel[status.status] ?? status.status}</Badge>
              {status.salesOrderNumber && (
                <div className="muted" style={{ fontSize: 14 }}>
                  ERP reference <span className="mono" style={{ color: 'var(--ink)' }}>{status.salesOrderNumber}</span>
                </div>
              )}
            </div>
            {status.message && <p className="muted" style={{ marginTop: 12 }}>{status.message}</p>}
            {status.remainingBackorder != null && status.remainingBackorder > 0 && (
              <p style={{ marginTop: 8, color: 'var(--warn)', fontSize: 14 }}>
                {status.remainingBackorder} unit(s) on backorder — shipping when supply lands.
              </p>
            )}
          </div>

          {status.fulfilments && status.fulfilments.length > 0 ? (
            <>
              <Eyebrow>Shipments</Eyebrow>
              <div className="cart-list" style={{ marginTop: 12 }}>
                {status.fulfilments.map((f, i) => (
                  <div className="cart-row" key={`${f.trackingNumber}-${i}`} style={{ alignItems: 'flex-start' }}>
                    <span className="icon-btn" style={{ background: 'var(--bg-soft)', flex: '0 0 auto' }}><TruckIcon size={20} /></span>
                    <div className="grow">
                      <div className="title">Shipment {i + 1}</div>
                      <div className="mono" style={{ color: 'var(--ink-2)', fontSize: 13, marginTop: 2 }}>
                        Tracking {f.trackingNumber}
                      </div>
                      <div className="muted" style={{ fontSize: 13, marginTop: 6 }}>
                        {f.lines.map((l) => `${l.itemNumber} ×${l.quantity}`).join(' · ')}
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            </>
          ) : (
            // Terminal states (Cancelled/Returned) aren't "being prepared" — don't imply they are.
            !isTerminal && (
              <>
                <Eyebrow>Shipments</Eyebrow>
                <p className="muted" style={{ marginTop: 10 }}>No shipments yet — your order is still being prepared.</p>
              </>
            )
          )}
        </>
      )}
    </div>
  )
}
