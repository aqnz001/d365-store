import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { startCheckout, pay, type CheckoutResult, type PayResult } from '../api'
import { Banner, CheckoutBadge, CreditBadge, Eyebrow, Spinner, CheckIcon } from '../components/ui'
import { ArrowRight, LockIcon } from '../components/icons'
import { useCart } from '../context/cart'
import { formatMoney } from '../format'
import { stripeEnabled } from '../payments'
import { StripeCardForm } from '../components/StripeCardForm'

type Stage = 'review' | 'payment' | 'done'
type DotState = 'ok' | 'danger' | 'warn' | 'pending'

const STEPS = ['Review', 'Payment', 'Confirmation']
const stageIndex: Record<Stage, number> = { review: 0, payment: 1, done: 2 }

const dotGlyph: Record<DotState, string> = { ok: '', danger: '✕', warn: '!', pending: '·' }
const dotState: Record<DotState, string> = { ok: 'Passed', danger: 'Failed', warn: 'Needs approval', pending: 'Pending' }

function GateRow({ state, label, detail }: { state: DotState; label: string; detail: string }) {
  return (
    <div className="gate-row">
      <span className={`gate-dot ${state}`} role="img" aria-label={dotState[state]}>
        {state === 'ok' ? <CheckIcon size={11} /> : dotGlyph[state]}
      </span>
      <span className="g-label">{label}</span>
      <span className="g-detail">{detail}</span>
    </div>
  )
}

function formatTtl(seconds: number): string {
  const m = Math.floor(seconds / 60)
  const s = seconds % 60
  return `${m}:${s.toString().padStart(2, '0')}`
}

function GateStatus({ checkout, ttl }: { checkout?: CheckoutResult; ttl: number }) {
  const availOk = checkout ? checkout.status !== 'AvailabilityBlocked' && checkout.status !== 'Shortfall' : false
  const creditState: DotState = !checkout?.pricing
    ? 'pending'
    : checkout.pricing.decision === 'Approved'
      ? 'ok'
      : checkout.pricing.decision === 'RequiresApproval'
        ? 'warn'
        : 'danger'
  // A soft reservation only counts while its TTL is still live (an expired hold is no longer valid).
  const reserved = (checkout?.reservationIds.length ?? 0) > 0 && ttl > 0
  const reservationState: DotState = !checkout ? 'pending' : reserved ? 'ok' : 'danger'

  return (
    <div className="gate live-rule">
      <h3>Gate status</h3>
      <GateRow
        state={!checkout ? 'pending' : availOk ? 'ok' : 'danger'}
        label="Availability"
        detail={!checkout ? '—' : availOk ? 'live ATP ✓' : 'shortfall'}
      />
      <GateRow
        state={!checkout?.pricing ? 'pending' : 'ok'}
        label="Pricing"
        detail={checkout?.pricing ? 'locked' : '—'}
      />
      <GateRow
        state={creditState}
        label="Credit"
        detail={checkout?.pricing ? checkout.pricing.creditStatus : '—'}
      />
      <GateRow
        state={reservationState}
        label="Soft reservation"
        detail={!checkout ? '—' : !reserved ? 'expired' : `RES-TTL ${formatTtl(ttl)}`}
      />
    </div>
  )
}

export function Checkout() {
  const { refresh } = useCart()
  const [stage, setStage] = useState<Stage>('review')
  const [checkout, setCheckout] = useState<CheckoutResult>()
  const [order, setOrder] = useState<PayResult>()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()
  const [ttl, setTtl] = useState(900)

  // Soft-reservation countdown — illustrative of the TTL that protects the held stock.
  useEffect(() => {
    if (!checkout?.reservationIds.length || stage === 'done') return
    const id = setInterval(() => setTtl((t) => Math.max(0, t - 1)), 1000)
    return () => clearInterval(id)
  }, [checkout, stage])

  const review = async () => {
    setBusy(true)
    setError(undefined)
    try {
      setTtl(900)
      setCheckout(await startCheckout())
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  const placeOrder = async (paymentToken: string) => {
    if (!checkout) return
    setBusy(true)
    setError(undefined)
    try {
      // The BFF re-resolves and charges the authoritative amount server-side; the amount here is for
      // display parity. paymentToken is a Stripe PaymentMethod id (pm_…) in prod, or 'ok' in dev.
      const result = await pay({ amount: orderTotal, currency: 'GBP', paymentToken, reservationIds: checkout.reservationIds })
      setOrder(result)
      setStage('done')
      void refresh()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  const current = stageIndex[stage]
  const ready = checkout?.status === 'Ready'
  const expired = (checkout?.reservationIds.length ?? 0) > 0 && ttl === 0
  const pricedLines = checkout?.pricing?.lines ?? []
  const orderTotal = pricedLines.reduce((sum, line) => sum + line.netEffectivePrice, 0)
  const hasPricing = pricedLines.length > 0

  const rerun = () => {
    setStage('review')
    void review()
  }

  return (
    <div className="container page" style={{ maxWidth: 880 }}>
      <div className="page-head">
        <Eyebrow accent>Checkout gate</Eyebrow>
        <h1>Confirm &amp; place your order</h1>
      </div>

      <div className="steps">
        {STEPS.map((label, i) => {
          const state = current > i ? 'done' : current === i ? 'active' : ''
          return (
            <div style={{ display: 'contents' }} key={label}>
              {i > 0 && <span className={`step-line${current >= i ? ' filled' : ''}`} />}
              <div className={`step ${state}`}>
                <span className="marker">{current > i ? <CheckIcon size={13} /> : i + 1}</span>
                <span className="label">{label}</span>
              </div>
            </div>
          )
        })}
      </div>

      {error && <Banner kind="danger">{error}</Banner>}

      {stage === 'review' && (
        <>
          {checkout && checkout.status !== 'Ready' && (
            <Banner kind={checkout.status === 'Shortfall' ? 'warn' : 'danger'}>
              <strong>
                <CheckoutBadge status={checkout.status} />
              </strong>{' '}
              {checkout.message ?? 'This order cannot proceed yet — adjust your cart and try again.'}
            </Banner>
          )}
          {expired && (
            <Banner kind="warn">Your soft reservation has expired — re-run the gate to hold stock again.</Banner>
          )}
          <div className="checkout-grid">
            <div className="panel panel-pad">
              <h2 style={{ fontSize: 20, marginBottom: 8 }}>Run the live gate</h2>
              <p className="muted" style={{ marginBottom: 16 }}>
                We confirm live availability, lock contract pricing, check your credit, and place a soft
                reservation — all before any payment is taken.
              </p>
              {ready && ttl > 0 ? (
                <button className="btn btn-primary" onClick={() => setStage('payment')}>
                  Continue to payment <ArrowRight size={16} />
                </button>
              ) : (
                <button className="btn btn-primary" onClick={review} disabled={busy}>
                  {busy ? <Spinner /> : checkout ? 'Re-run gate' : 'Review order'}
                </button>
              )}
            </div>
            <GateStatus checkout={checkout} ttl={ttl} />
          </div>
        </>
      )}

      {stage === 'payment' && checkout && (
        <div className="checkout-grid">
          <div className="panel panel-pad">
            <h2 style={{ fontSize: 20, marginBottom: 14 }}>Payment</h2>
            {expired ? (
              <>
                <Banner kind="warn">Your reservation expired before payment — re-run the gate to continue.</Banner>
                <button className="btn btn-primary btn-lg" onClick={rerun} style={{ marginTop: 4 }}>
                  Re-run gate <ArrowRight size={16} />
                </button>
              </>
            ) : stripeEnabled ? (
              <StripeCardForm
                onPay={placeOrder}
                busy={busy}
                payLabel={hasPricing ? `Pay ${formatMoney(orderTotal)} & place order` : 'Pay & place order'}
              />
            ) : (
              <div className="pay-field">
                <div className="card-line">
                  <LockIcon size={16} /> 4242 4242 4242 4242
                  <span style={{ marginLeft: 'auto', color: 'var(--ink-2)' }}>12 / 28</span>
                </div>
                <p className="pay-note">
                  <LockIcon size={14} /> Demo mode — set VITE_STRIPE_PUBLISHABLE_KEY for live Stripe card
                  capture (PCI SAQ-A). Card details never touch our servers.
                </p>
                <button className="btn btn-primary btn-lg" onClick={() => placeOrder('ok')} disabled={busy} style={{ marginTop: 18 }}>
                  {busy ? <Spinner /> : hasPricing ? `Pay ${formatMoney(orderTotal)} & place order` : 'Pay & place order'}
                </button>
              </div>
            )}
          </div>
          <div className="gate live-rule">
            <h3>Locked order</h3>
            <div className="gate-row">
              <span className="g-label">Availability</span>
              <span className="g-detail">
                <CheckoutBadge status={checkout.status} />
              </span>
            </div>
            {checkout.pricing && (
              <div className="gate-row">
                <span className="g-label">Credit</span>
                <span className="g-detail">
                  <CreditBadge decision={checkout.pricing.decision} label={checkout.pricing.creditStatus} />
                </span>
              </div>
            )}
            <div className="gate-row">
              <span className="g-label">Reservation</span>
              <span className="g-detail">RES-TTL {formatTtl(ttl)}</span>
            </div>
            {hasPricing && (
              <>
                <div style={{ borderTop: '1px solid var(--hairline)', margin: '10px 0 8px' }} />
                {pricedLines.map((line) => (
                  <div className="gate-row" key={line.itemNumber} style={{ borderBottom: 'none', padding: '4px 0' }}>
                    <span className="g-label mono" style={{ fontSize: 12.5 }}>
                      {line.itemNumber} <span className="muted">×{line.quantity}</span>
                    </span>
                    <span className="g-detail tnum">{formatMoney(line.netEffectivePrice)}</span>
                  </div>
                ))}
                <div className="row" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', marginTop: 8 }}>
                  <span className="eyebrow accent">Order total</span>
                  <span className="serif tnum" style={{ fontSize: 24 }}>{formatMoney(orderTotal)}</span>
                </div>
                <p className="muted" style={{ fontSize: 12, marginTop: 4 }}>Contract net price — your charge today.</p>
              </>
            )}
          </div>
        </div>
      )}

      {stage === 'done' && order && (
        <div className="panel panel-pad" style={{ textAlign: 'center', padding: '40px 24px' }}>
          {order.status === 'OrderPlaced' ? (
            <>
              <Eyebrow accent>Confirmed</Eyebrow>
              <h2 style={{ fontSize: 30, margin: '8px 0 6px' }}>Order placed</h2>
              <p className="muted" style={{ marginBottom: 4 }}>Your order reference</p>
              <div className="order-ref-chip">{order.orderReference}</div>
              <p className="muted" style={{ maxWidth: 440, margin: '0 auto 18px' }}>
                Queued for processing through the order writeback. You can track its status any time under your
                account.
              </p>
              <Link className="btn btn-primary" to="/account">
                View in account <ArrowRight size={16} />
              </Link>
            </>
          ) : (
            <Banner kind="danger">{order.message ?? order.status}</Banner>
          )}
        </div>
      )}
    </div>
  )
}
