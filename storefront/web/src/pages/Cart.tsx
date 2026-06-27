import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { getCatalog, removeFromCart, validateCart, type CatalogProduct, type CartValidateResponse } from '../api'
import { Banner, BandBadge, EmptyState, Eyebrow, Loading, Skeleton, Spinner } from '../components/ui'
import { ProductMedia } from '../components/ProductMedia'
import { ArrowRight, ShieldIcon, TrashIcon } from '../components/icons'
import { useCart } from '../context/cart'

export function Cart() {
  const { cart, loading, applyCart } = useCart()
  const [catalog, setCatalog] = useState<Record<string, CatalogProduct>>({})
  const [validation, setValidation] = useState<CartValidateResponse>()
  const [validating, setValidating] = useState(false)
  const [error, setError] = useState<string>()

  useEffect(() => {
    getCatalog()
      .then((products) => setCatalog(Object.fromEntries(products.map((p) => [p.sku, p]))))
      .catch(() => undefined)
  }, [])

  const remove = async (index: number) => {
    try {
      applyCart(await removeFromCart(index))
      setValidation(undefined)
    } catch (e) {
      setError((e as Error).message)
    }
  }

  const validate = async () => {
    setValidating(true)
    setError(undefined)
    try {
      setValidation(await validateCart())
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setValidating(false)
    }
  }

  // Validation lines are returned in cart order, so align by index — itemNumber alone is ambiguous
  // when the same part appears on more than one line/site.
  const bandFor = (index: number) => validation?.lines[index]?.band
  const totalUnits = useMemo(() => cart?.lines.reduce((sum, l) => sum + l.quantity, 0) ?? 0, [cart])
  const inStockCount = validation?.lines.filter((l) => l.band === 'InStock').length ?? 0

  if (loading && !cart) {
    return (
      <div className="container page">
        <Loading>Loading your cart…</Loading>
      </div>
    )
  }

  const empty = !cart || cart.lines.length === 0

  return (
    <div className="container page">
      <div className="page-head">
        <Eyebrow accent>Your order</Eyebrow>
        <h1>Cart</h1>
      </div>
      {error && <Banner kind="danger">{error}</Banner>}

      {empty ? (
        <EmptyState title="Your cart is empty">
          Nothing here yet. <Link to="/">Browse the catalog</Link> to start an order.
        </EmptyState>
      ) : (
        <div className="cart-layout">
          <div className="cart-list">
            {cart.lines.map((line, index) => {
              const product = catalog[line.itemNumber]
              const band = bandFor(index)
              return (
                <div className="cart-row" key={`${line.itemNumber}-${index}`}>
                  <ProductMedia
                    productType={product?.productType ?? ''}
                    sku={line.itemNumber}
                    variant="thumb"
                  />
                  <div className="grow">
                    <div className="title">{product?.title ?? line.itemNumber}</div>
                    <div className="sku mono" style={{ color: 'var(--ink-2)', fontSize: 12.5 }}>
                      {line.itemNumber}
                    </div>
                    <div style={{ marginTop: 4 }}>
                      <Eyebrow>
                        Qty {line.quantity} · Site {line.site}
                      </Eyebrow>
                    </div>
                  </div>
                  <div className="band-cell" style={{ minWidth: 96, textAlign: 'right' }}>
                    {validating ? (
                      <Skeleton width={96} height={24} />
                    ) : band ? (
                      <span className="resolve-in" style={{ display: 'inline-block' }}>
                        <BandBadge band={band} />
                      </span>
                    ) : null}
                  </div>
                  <button className="btn-link" onClick={() => remove(index)} aria-label={`Remove ${product?.title ?? line.itemNumber}`}>
                    <TrashIcon size={15} /> Remove
                  </button>
                </div>
              )
            })}
          </div>

          <aside className="summary live-rule">
            <p className="sr-only" role="status" aria-live="polite">
              {validating ? 'Checking availability…' : validation ? `Availability checked: ${inStockCount} of ${cart.lines.length} line(s) in stock.` : ''}
            </p>
            <Eyebrow accent>Order summary</Eyebrow>
            <div className="row" style={{ marginTop: 8 }}>
              <span className="muted">Line items</span>
              <span className="tnum">{cart.lines.length}</span>
            </div>
            <div className="row">
              <span className="muted">Total units</span>
              <span className="tnum">{totalUnits}</span>
            </div>
            <hr className="divider" />
            <div className="row" style={{ alignItems: 'flex-end' }}>
              <span className="eyebrow">Order total</span>
              <span className="serif" style={{ fontSize: 19, color: 'var(--ink-2)' }}>At checkout</span>
            </div>
            <p className="muted" style={{ fontSize: 13, margin: '4px 0 14px' }}>
              Contract pricing and credit are resolved live at the checkout gate.
            </p>
            <button className="btn" onClick={validate} disabled={validating} style={{ width: '100%', marginBottom: 10 }}>
              {validating ? <Spinner /> : 'Check live availability'}
            </button>
            <Link className="btn btn-primary btn-block" to="/checkout">
              Proceed to checkout <ArrowRight size={16} />
            </Link>
            <p className="pay-note" style={{ justifyContent: 'center', marginTop: 14 }}>
              <ShieldIcon size={15} /> Availability &amp; price confirmed at checkout
            </p>
            {validation && (
              <p className="mono" style={{ fontSize: 11, color: 'var(--ink-2)', marginTop: 10, textAlign: 'center' }}>
                corr {validation.correlationId.slice(0, 12)}
              </p>
            )}
          </aside>
        </div>
      )}
    </div>
  )
}
