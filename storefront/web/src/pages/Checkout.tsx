import { useState } from 'react'
import { startCheckout, pay, type CheckoutResult, type PayResult } from '../api'
import { Spinner, Banner, CheckoutBadge, CreditBadge } from '../components/ui'

type Stage = 'review' | 'payment' | 'done'

export function Checkout() {
  const [stage, setStage] = useState<Stage>('review')
  const [checkout, setCheckout] = useState<CheckoutResult>()
  const [order, setOrder] = useState<PayResult>()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()

  const review = async () => {
    setBusy(true)
    setError(undefined)
    try {
      const result = await startCheckout()
      setCheckout(result)
      if (result.status === 'Ready') setStage('payment')
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  const placeOrder = async () => {
    if (!checkout) return
    setBusy(true)
    setError(undefined)
    try {
      const result = await pay({ amount: 0, currency: 'GBP', paymentToken: 'ok', reservationIds: checkout.reservationIds })
      setOrder(result)
      setStage('done')
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <section>
      <div className="page-head">
        <h1>Checkout</h1>
      </div>
      <div className="steps">
        <div className={`step ${stage === 'review' ? 'active' : 'done'}`}>
          <span className="n">1</span> Review
        </div>
        <div className={`step ${stage === 'payment' ? 'active' : stage === 'done' ? 'done' : ''}`}>
          <span className="n">2</span> Payment
        </div>
        <div className={`step ${stage === 'done' ? 'active' : ''}`}>
          <span className="n">3</span> Confirmation
        </div>
      </div>
      {error && <Banner kind="danger">{error}</Banner>}

      {stage === 'review' && (
        <div className="summary">
          <p className="muted">
            Confirm live availability, pricing and credit, and place a soft reservation — all before payment.
          </p>
          {checkout && checkout.status !== 'Ready' && (
            <Banner kind="danger">
              {checkout.message} <CheckoutBadge status={checkout.status} />
            </Banner>
          )}
          <button className="btn btn-primary" onClick={review} disabled={busy}>
            {busy ? <Spinner /> : 'Review order'}
          </button>
        </div>
      )}

      {stage === 'payment' && checkout && (
        <div className="summary">
          <div className="row">
            <span>Availability</span>
            <CheckoutBadge status={checkout.status} />
          </div>
          {checkout.pricing && (
            <div className="row">
              <span>Credit</span>
              <CreditBadge decision={checkout.pricing.decision} label={checkout.pricing.creditStatus} />
            </div>
          )}
          <div className="row">
            <span>Reservations</span>
            <span className="muted">{checkout.reservationIds.length} placed</span>
          </div>
          <hr style={{ border: 'none', borderTop: '1px solid var(--border)', margin: '14px 0' }} />
          <p className="muted" style={{ fontSize: 13 }}>
            Card details are entered in the provider's hosted Payment Element — your card never touches our servers.
          </p>
          <div style={{ border: '1px dashed var(--border)', borderRadius: 8, padding: 14, color: 'var(--muted)', fontSize: 14, margin: '8px 0 14px', fontFamily: 'ui-monospace, monospace' }}>
            •••• •••• •••• 4242 — Stripe Payment Element
          </div>
          <button className="btn btn-primary" onClick={placeOrder} disabled={busy}>
            {busy ? <Spinner /> : 'Pay & place order'}
          </button>
        </div>
      )}

      {stage === 'done' && order && (
        <div className="summary">
          {order.status === 'OrderPlaced' ? (
            <>
              <Banner kind="ok">Order placed.</Banner>
              <div className="row">
                <span>Order reference</span>
                <strong>{order.orderReference}</strong>
              </div>
              <p className="muted" style={{ fontSize: 13 }}>Queued for processing — track it under Account.</p>
            </>
          ) : (
            <Banner kind="danger">{order.message ?? order.status}</Banner>
          )}
        </div>
      )}
    </section>
  )
}
