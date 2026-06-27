import { useState } from 'react'
import { CardElement, Elements, useElements, useStripe } from '@stripe/react-stripe-js'
import { getStripe } from '../payments'
import { Banner, Spinner } from './ui'
import { LockIcon } from './icons'

// Stripe-hosted card capture (PCI SAQ-A): the card never touches our servers. On submit we mint a
// PaymentMethod (pm_…) and hand it to onPay; the BFF confirms the PaymentIntent server-side.
function CardForm({ onPay, busy, payLabel }: { onPay: (token: string) => Promise<void>; busy: boolean; payLabel: string }) {
  const stripe = useStripe()
  const elements = useElements()
  const [error, setError] = useState<string>()
  const [working, setWorking] = useState(false)

  const submit = async () => {
    if (!stripe || !elements) return
    const card = elements.getElement(CardElement)
    if (!card) return

    setWorking(true)
    setError(undefined)
    const result = await stripe.createPaymentMethod({ type: 'card', card })
    if (result.error || !result.paymentMethod) {
      setError(result.error?.message ?? 'Could not read the card. Please check the details.')
      setWorking(false)
      return
    }

    try {
      await onPay(result.paymentMethod.id)
    } finally {
      setWorking(false)
    }
  }

  return (
    <div className="pay-field">
      <div style={{ border: '1px solid var(--hairline)', borderRadius: 'var(--radius-sm)', padding: '14px', background: 'var(--surface)' }}>
        <CardElement
          options={{
            hidePostalCode: false,
            style: {
              base: { fontSize: '15px', color: '#1a1a1a', fontFamily: 'Inter, system-ui, sans-serif', '::placeholder': { color: '#6b6b66' } },
              invalid: { color: '#a52213' },
            },
          }}
        />
      </div>
      <p className="pay-note">
        <LockIcon size={14} /> Card details are entered in Stripe's hosted fields — they never touch our servers (PCI SAQ-A).
      </p>
      {error && <Banner kind="danger">{error}</Banner>}
      <button className="btn btn-primary btn-lg" onClick={submit} disabled={busy || working || !stripe} style={{ marginTop: 14 }}>
        {busy || working ? <Spinner /> : payLabel}
      </button>
    </div>
  )
}

/** Card capture wrapped in the Stripe Elements provider. */
export function StripeCardForm(props: { onPay: (token: string) => Promise<void>; busy: boolean; payLabel: string }) {
  return (
    <Elements stripe={getStripe()}>
      <CardForm {...props} />
    </Elements>
  )
}
