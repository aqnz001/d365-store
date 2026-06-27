// Store currency is GBP (matches the seeded pricing + Stripe currency). One place to change it.
const money = new Intl.NumberFormat('en-GB', { style: 'currency', currency: 'GBP' })

/** Formats an amount as GBP, or a dash when the price is unknown (no fabricated zero). */
export function formatMoney(amount: number | null | undefined): string {
  return amount == null ? '—' : money.format(amount)
}
