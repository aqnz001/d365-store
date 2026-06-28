// Typed client for the BFF. Cookie session (credentials: include); in dev a customer header
// stands in for Entra SSO (DR-004). In production builds the header is NOT sent — auth is the
// Entra session cookie, and the BFF ignores X-Dev-Customer when Auth:Mode=Entra.
const DEV_CUSTOMER = 'C-DEV'
const devHeaders: Record<string, string> = import.meta.env.DEV ? { 'X-Dev-Customer': DEV_CUSTOMER } : {}

/** Sends the browser to the BFF login endpoint (which challenges Entra), returning here after. */
export function redirectToLogin(): void {
  const returnUrl = encodeURIComponent(window.location.pathname + window.location.search)
  window.location.href = `/api/auth/login?returnUrl=${returnUrl}`
}

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`/api${path}`, {
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      ...devHeaders,
      ...(init?.headers ?? {}),
    },
    ...init,
  })
  if (response.status === 401) {
    // Session absent or expired — send the user to sign in (no-op in dev, always authenticated).
    redirectToLogin()
    throw new Error('unauthenticated')
  }
  if (!response.ok) {
    throw new Error(`${init?.method ?? 'GET'} /api${path} → ${response.status}`)
  }
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}

export interface CatalogMetafields {
  unit: string
  orderMultiple: number
  minOrderQty: number
  backorderable: boolean
}

export interface CatalogProduct {
  sku: string
  title: string
  bodyHtml: string
  productType: string
  status: string
  metafields: CatalogMetafields
  // Indicative list price (catalog attribute). The contract net price is resolved live at checkout.
  listPrice?: number | null
  // Synced ADVISORY availability band (TDD §5.2) — display only; the live IVS check at the gate is
  // authoritative (Golden Rule #5). Sentence-case band words, never a raw count (#4).
  availabilityBand?: string | null
}

export interface CartLine {
  itemNumber: string
  quantity: number
  site: string
}

export interface ShoppingCart {
  lines: CartLine[]
}

export interface CartValidateLineResult {
  itemNumber: string
  band: string
  decision: string
  availableQuantity: number
}

export interface CartValidateResponse {
  correlationId: string
  lines: CartValidateLineResult[]
}

export interface PricedLine {
  itemNumber: string
  quantity: number
  unitPrice: number
  netEffectivePrice: number
}

export interface CheckoutResult {
  status: string
  reservationIds: string[]
  message?: string
  pricing?: { customerAccount: string; creditStatus: string; decision: string; lines: PricedLine[] }
}

export interface PayResult {
  status: string
  orderReference?: string
  message?: string
}

export interface PlacedOrder {
  orderReference: string
  placedAtUtc: string
}

export interface CreditStanding {
  customerAccount: string
  creditStatus: string
  decision: string
}

export interface CurrentUser {
  customerAccount: string
  name?: string
  email?: string
}

export interface OrderFulfilment {
  trackingNumber: string
  lines: { itemNumber: string; quantity: number }[]
}

export interface OrderStatus {
  orderId: string
  salesOrderNumber?: string | null
  status: string
  message?: string
  remainingBackorder?: number | null
  fulfilments?: OrderFulfilment[]
}

/** Probes the session without redirecting — returns the user, or null when not signed in. */
export async function probeAuth(): Promise<CurrentUser | null> {
  const response = await fetch('/api/me', {
    credentials: 'include',
    headers: { ...devHeaders },
  })
  if (response.status === 401) return null
  if (!response.ok) throw new Error(`GET /api/me → ${response.status}`)
  return (await response.json()) as CurrentUser
}

export const getMe = () => api<CurrentUser>('/me')
export const getProduct = (sku: string) => api<CatalogProduct>(`/catalog/${encodeURIComponent(sku)}`)

/** Live order status; null when no status has been recorded yet (404). */
export async function getOrderStatus(reference: string): Promise<OrderStatus | null> {
  try {
    return await api<OrderStatus>(`/account/orders/${encodeURIComponent(reference)}/status`)
  } catch (e) {
    if (String((e as Error).message).includes('→ 404')) return null
    throw e
  }
}

export const getCatalog = () => api<CatalogProduct[]>('/catalog')
export const addToCart = (line: CartLine) =>
  api<ShoppingCart>('/cart/items', { method: 'POST', body: JSON.stringify(line) })
export const getCart = () => api<ShoppingCart>('/cart')
export const removeFromCart = (index: number) => api<ShoppingCart>(`/cart/items/${index}`, { method: 'DELETE' })
export const clearCart = () => api<void>('/cart', { method: 'DELETE' })
export const validateCart = () => api<CartValidateResponse>('/cart/validate', { method: 'POST' })
export const startCheckout = () => api<CheckoutResult>('/checkout/start', { method: 'POST' })
export const pay = (body: { amount: number; currency: string; paymentToken: string; reservationIds: string[] }) =>
  api<PayResult>('/checkout/pay', { method: 'POST', body: JSON.stringify(body) })
export const getOrders = () => api<PlacedOrder[]>('/account/orders')
export const getCredit = () => api<CreditStanding>('/account/credit')
