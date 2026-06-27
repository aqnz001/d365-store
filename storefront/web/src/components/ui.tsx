import type { ReactNode } from 'react'

export function Spinner() {
  return <span className="spinner" role="status" aria-label="Loading" />
}

export function Banner({ kind, children }: { kind: 'danger' | 'info' | 'ok'; children: ReactNode }) {
  return <div className={`banner banner-${kind}`}>{children}</div>
}

export function EmptyState({ children }: { children: ReactNode }) {
  return <div className="empty">{children}</div>
}

const bandTone: Record<string, string> = {
  InStock: 'ok',
  LowStock: 'warn',
  Backorder: 'info',
  MadeToOrder: 'info',
  Unavailable: 'danger',
}

const creditTone: Record<string, string> = {
  Approved: 'ok',
  RequiresApproval: 'warn',
  Blocked: 'danger',
}

const checkoutTone: Record<string, string> = {
  Ready: 'ok',
  AvailabilityBlocked: 'danger',
  CreditBlocked: 'danger',
  Shortfall: 'warn',
}

export function Badge({ tone, children }: { tone: string; children: ReactNode }) {
  return <span className={`badge badge-${tone}`}>{children}</span>
}

export function BandBadge({ band }: { band: string }) {
  return <Badge tone={bandTone[band] ?? 'muted'}>{band}</Badge>
}

export function CreditBadge({ decision, label }: { decision: string; label?: string }) {
  return <Badge tone={creditTone[decision] ?? 'muted'}>{label ?? decision}</Badge>
}

export function CheckoutBadge({ status }: { status: string }) {
  return <Badge tone={checkoutTone[status] ?? 'muted'}>{status}</Badge>
}
