/// <reference types="vite/client" />

interface ImportMetaEnv {
  // Stripe publishable key (pk_…). Not a secret; present ⇒ real Stripe card capture,
  // absent ⇒ the dev/test fake-token path. See deploy/web.env.sample.
  readonly VITE_STRIPE_PUBLISHABLE_KEY?: string
}
