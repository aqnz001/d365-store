import { loadStripe, type Stripe } from '@stripe/stripe-js'

// Publishable key (pk_…) is safe in the client. When set, the SPA collects the card via Stripe's
// hosted Card Element (PCI SAQ-A) and sends a PaymentMethod id (pm_…) to the BFF, which confirms a
// PaymentIntent server-side. When unset, checkout uses the dev/test fake-token path.
const publishableKey = import.meta.env.VITE_STRIPE_PUBLISHABLE_KEY

export const stripeEnabled: boolean = Boolean(publishableKey)

let stripePromise: Promise<Stripe | null> | undefined

/** Lazily loads Stripe.js once. Returns null when no publishable key is configured. */
export function getStripe(): Promise<Stripe | null> {
  if (!publishableKey) {
    return Promise.resolve(null)
  }
  stripePromise ??= loadStripe(publishableKey)
  return stripePromise
}
