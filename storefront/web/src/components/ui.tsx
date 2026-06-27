import type { ReactNode } from 'react'
import { CheckIcon } from './icons'

export function Spinner() {
  return <span className="spinner" role="status" aria-label="Loading" />
}

export function Loading({ children = 'Loading…' }: { children?: ReactNode }) {
  return (
    <div className="loading-row">
      <Spinner /> {children}
    </div>
  )
}

export function Eyebrow({ accent, children }: { accent?: boolean; children: ReactNode }) {
  return <span className={`eyebrow${accent ? ' accent' : ''}`}>{children}</span>
}

const bannerIcon: Record<string, string> = { danger: '!', warn: '!', info: 'i', ok: '✓' }

export function Banner({ kind, children }: { kind: 'danger' | 'info' | 'ok' | 'warn'; children: ReactNode }) {
  return (
    <div className={`banner banner-${kind}`} role={kind === 'danger' ? 'alert' : undefined}>
      <span className="ico" aria-hidden="true">
        {kind === 'ok' ? <CheckIcon /> : bannerIcon[kind]}
      </span>
      <div>{children}</div>
    </div>
  )
}

export function EmptyState({ title, children }: { title?: string; children: ReactNode }) {
  return (
    <div className="empty">
      {title && <h2>{title}</h2>}
      <p className="muted">{children}</p>
    </div>
  )
}

export function Skeleton({ width = 84, height = 22 }: { width?: number; height?: number }) {
  return <span className="skeleton" style={{ width, height }} aria-hidden="true" />
}

const bandTone: Record<string, string> = {
  InStock: 'ok',
  LowStock: 'warn',
  Backorder: 'info',
  MadeToOrder: 'info',
  Unavailable: 'danger',
}

// Sentence-case copy — availability is a band, never a raw count (Golden Rule #4).
const bandLabel: Record<string, string> = {
  InStock: 'In stock',
  LowStock: 'Low stock',
  Backorder: 'Backorder',
  MadeToOrder: 'Made to order',
  Unavailable: 'Unavailable',
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

const checkoutLabel: Record<string, string> = {
  Ready: 'Ready to place',
  AvailabilityBlocked: 'Availability issue',
  CreditBlocked: 'Credit hold',
  Shortfall: 'Stock shortfall',
}

export function Badge({ tone, onMedia, children }: { tone: string; onMedia?: boolean; children: ReactNode }) {
  return <span className={`badge badge-${tone}${onMedia ? ' on-media' : ''}`}>{children}</span>
}

export function BandBadge({ band, onMedia }: { band: string; onMedia?: boolean }) {
  return (
    <Badge tone={bandTone[band] ?? 'muted'} onMedia={onMedia}>
      {bandLabel[band] ?? band}
    </Badge>
  )
}

export function CreditBadge({ decision, label }: { decision: string; label?: string }) {
  return <Badge tone={creditTone[decision] ?? 'muted'}>{label ?? decision}</Badge>
}

export function CheckoutBadge({ status }: { status: string }) {
  return <Badge tone={checkoutTone[status] ?? 'muted'}>{checkoutLabel[status] ?? status}</Badge>
}

export { CheckIcon }
