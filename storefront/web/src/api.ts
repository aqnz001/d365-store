// Typed client for the BFF. Cookie session (credentials: include); in dev a customer header
// stands in for Entra SSO (DR-004) — production drops it and relies on the auth cookie.
const DEV_CUSTOMER = 'C-DEV'

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`/api${path}`, {
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      'X-Dev-Customer': DEV_CUSTOMER,
      ...(init?.headers ?? {}),
    },
    ...init,
  })
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
}

export interface CartLine {
  itemNumber: string
  quantity: number
  site: string
}

export interface ShoppingCart {
  lines: CartLine[]
}

export interface CheckoutResult {
  status: string
  reservationIds: string[]
  message?: string
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

export const getCatalog = () => api<CatalogProduct[]>('/catalog')
export const addToCart = (line: CartLine) =>
  api<ShoppingCart>('/cart/items', { method: 'POST', body: JSON.stringify(line) })
export const getCart = () => api<ShoppingCart>('/cart')
export const startCheckout = () => api<CheckoutResult>('/checkout/start', { method: 'POST' })
export const pay = (body: { amount: number; currency: string; paymentToken: string; reservationIds: string[] }) =>
  api<PayResult>('/checkout/pay', { method: 'POST', body: JSON.stringify(body) })
export const getOrders = () => api<PlacedOrder[]>('/account/orders')
export const getCredit = () => api<CreditStanding>('/account/credit')
