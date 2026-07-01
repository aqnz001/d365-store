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
    // Surface the server's { message } (e.g. an approval conflict) when present, else a terse fallback.
    const detail = (await response.json().catch(() => null)) as { message?: string } | null
    throw new Error(detail?.message ?? `${init?.method ?? 'GET'} /api${path} → ${response.status}`)
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

export interface CatalogPage {
  items: CatalogProduct[]
  total: number
  page: number
  pageSize: number
  categories: string[]
}

export interface CatalogQuery {
  q?: string
  category?: string
  sort?: string
  page?: number
  pageSize?: number
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
  /** FinOps-owned tax for the line (the portal surfaces it, never computes it). Absent → 0. */
  taxRate?: number
  taxAmount?: number
}

export interface CheckoutResult {
  status: string
  reservationIds: string[]
  message?: string
  pricing?: { customerAccount: string; creditStatus: string; decision: string; lines: PricedLine[] }
  /** Whether net-terms (pay-on-account) may be offered — true only when credit is Approved. */
  allowOnAccount: boolean
}

export type SettlementMethod = 'Card' | 'OnAccount'

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
  /** FinOps-owned numeric credit (the portal surfaces, never computes). Absent → unknown. */
  creditLimit?: number | null
  availableCredit?: number | null
}

export interface Address {
  id: string
  name: string
  line1: string
  line2?: string | null
  city: string
  region?: string | null
  postalCode: string
  country: string
  isDefaultShipping: boolean
  isDefaultBilling: boolean
}

export type AddressInput = Omit<Address, 'id'>

export type CompanyRole = 'Admin' | 'Approver' | 'Buyer'

export interface CompanyMember {
  userId: string
  name: string
  role: CompanyRole
  spendLimit?: number | null
}

/** An over-spend-limit on-account order awaiting an approver (DR-027). */
export interface PendingApproval {
  id: string
  buyerUserId: string
  buyerName: string
  amount: number
  currency: string
  poNumber?: string | null
  lines: { itemNumber: string; quantity: number }[]
  placedAtUtc: string
  status: 'Pending' | 'Approved' | 'Rejected'
  decidedBy?: string | null
  orderReference?: string | null
}

export interface CurrentUser {
  customerAccount: string
  name?: string
  email?: string
  userId?: string
  role?: CompanyRole
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
export const getProduct = (sku: string) => api<CatalogProduct>(`/catalog/item/${encodeURIComponent(sku)}`)

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

/** Server-side catalog search/filter/sort/pagination — the browse page fetches one page at a time. */
export function searchCatalog(query: CatalogQuery = {}): Promise<CatalogPage> {
  const params = new URLSearchParams()
  if (query.q) params.set('q', query.q)
  if (query.category && query.category !== 'All') params.set('category', query.category)
  if (query.sort) params.set('sort', query.sort)
  if (query.page) params.set('page', String(query.page))
  if (query.pageSize) params.set('pageSize', String(query.pageSize))
  const qs = params.toString()
  return api<CatalogPage>(`/catalog/search${qs ? `?${qs}` : ''}`)
}
export const addToCart = (line: CartLine) =>
  api<ShoppingCart>('/cart/items', { method: 'POST', body: JSON.stringify(line) })
export const getCart = () => api<ShoppingCart>('/cart')
export const removeFromCart = (index: number) => api<ShoppingCart>(`/cart/items/${index}`, { method: 'DELETE' })
export const clearCart = () => api<void>('/cart', { method: 'DELETE' })
export const validateCart = () => api<CartValidateResponse>('/cart/validate', { method: 'POST' })
export const startCheckout = () => api<CheckoutResult>('/checkout/start', { method: 'POST' })
export const pay = (body: {
  amount: number
  currency: string
  paymentToken: string
  reservationIds: string[]
  paymentMethod?: SettlementMethod
  poNumber?: string
}) => api<PayResult>('/checkout/pay', { method: 'POST', body: JSON.stringify(body) })
export const getOrders = () => api<PlacedOrder[]>('/account/orders')
export const getCredit = () => api<CreditStanding>('/account/credit')

export const getMembers = () => api<CompanyMember[]>('/company/members')
export const addMember = (body: CompanyMember) =>
  api<CompanyMember>('/company/members', { method: 'POST', body: JSON.stringify(body) })
export const updateMember = (userId: string, body: CompanyMember) =>
  api<CompanyMember>(`/company/members/${encodeURIComponent(userId)}`, { method: 'PUT', body: JSON.stringify(body) })
export const deleteMember = (userId: string) =>
  api<void>(`/company/members/${encodeURIComponent(userId)}`, { method: 'DELETE' })

export const getApprovals = () => api<PendingApproval[]>('/company/approvals')
export const approveOrder = (id: string) =>
  api<{ orderReference?: string }>(`/company/approvals/${encodeURIComponent(id)}/approve`, { method: 'POST' })
export const rejectOrder = (id: string) =>
  api<void>(`/company/approvals/${encodeURIComponent(id)}/reject`, { method: 'POST' })

export const getAddresses = () => api<Address[]>('/account/addresses')
export const addAddress = (body: AddressInput) =>
  api<Address>('/account/addresses', { method: 'POST', body: JSON.stringify(body) })
export const updateAddress = (id: string, body: AddressInput) =>
  api<Address>(`/account/addresses/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify(body) })
export const deleteAddress = (id: string) =>
  api<void>(`/account/addresses/${encodeURIComponent(id)}`, { method: 'DELETE' })
