import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { getProduct, addToCart, type CatalogProduct } from '../api'
import { Banner, BandBadge, Eyebrow, Loading, Spinner, CheckIcon } from '../components/ui'
import { ProductMedia } from '../components/ProductMedia'
import { MinusIcon, PlusIcon } from '../components/icons'
import { useCart } from '../context/cart'
import { formatMoney } from '../format'

export function ProductDetail() {
  const { sku = '' } = useParams()
  const [product, setProduct] = useState<CatalogProduct | null>()
  const [error, setError] = useState<string>()

  useEffect(() => {
    let ignore = false
    setProduct(undefined)
    setError(undefined)
    getProduct(sku)
      .then((p) => {
        if (!ignore) setProduct(p)
      })
      .catch((e: Error) => {
        if (ignore) return
        if (e.message.includes('→ 404')) setProduct(null)
        else setError(e.message)
      })
    return () => {
      ignore = true
    }
  }, [sku])

  if (error) {
    return (
      <div className="container page">
        <Banner kind="danger">Could not load the product: {error}</Banner>
      </div>
    )
  }
  if (product === undefined) {
    return (
      <div className="container page">
        <Loading>Loading product…</Loading>
      </div>
    )
  }
  if (product === null) {
    return (
      <div className="container page">
        <div className="empty">
          <h2>Product not found</h2>
          <p className="muted">
            “{sku}” isn’t in the catalog. <Link to="/">Back to the catalog</Link>.
          </p>
        </div>
      </div>
    )
  }

  return (
    <div className="container page">
      <p style={{ marginBottom: 18 }}>
        <Link to="/" className="muted" style={{ fontSize: 14 }}>← Back to catalog</Link>
      </p>
      <div className="pdp-grid">
        <div className="pdp-media">
          <ProductMedia productType={product.productType} sku={product.sku}>
            {product.availabilityBand && <BandBadge band={product.availabilityBand} onMedia />}
          </ProductMedia>
        </div>
        <ProductBuy product={product} />
      </div>
    </div>
  )
}

function ProductBuy({ product }: { product: CatalogProduct }) {
  const min = product.metafields.minOrderQty || 1
  const step = product.metafields.orderMultiple || 1
  const [qty, setQty] = useState(min)
  const [adding, setAdding] = useState(false)
  const [added, setAdded] = useState(false)
  const { applyCart, openDrawer } = useCart()

  const clamp = (value: number) => {
    const k = Math.max(0, Math.round((value - min) / step))
    return Number((min + k * step).toFixed(4))
  }
  const offStep = qty < min || Math.abs(qty - clamp(qty)) > 1e-6

  const add = async () => {
    setAdding(true)
    try {
      applyCart(await addToCart({ itemNumber: product.sku, quantity: qty, site: '1' }))
      setAdded(true)
      openDrawer()
      setTimeout(() => setAdded(false), 1400)
    } finally {
      setAdding(false)
    }
  }

  return (
    <div className="pdp-info">
      <Eyebrow accent>{product.productType}</Eyebrow>
      <h1 style={{ fontSize: 34, margin: '8px 0 6px' }}>{product.title}</h1>
      <div className="mono" style={{ color: 'var(--ink-2)', fontSize: 13 }}>{product.sku}</div>

      <div style={{ display: 'flex', alignItems: 'center', gap: 12, margin: '16px 0' }}>
        {product.listPrice != null ? (
          <div style={{ fontSize: 26, fontWeight: 800, letterSpacing: '-0.02em' }}>
            {formatMoney(product.listPrice)}{' '}
            <span style={{ fontSize: 14, fontWeight: 500, color: 'var(--ink-2)' }}>list / {product.metafields.unit}</span>
          </div>
        ) : (
          <div className="muted">Priced at checkout</div>
        )}
        {product.availabilityBand && <BandBadge band={product.availabilityBand} />}
      </div>

      {product.bodyHtml && <p style={{ color: 'var(--ink-2)', fontSize: 16, lineHeight: 1.6, marginBottom: 18 }}>{product.bodyHtml}</p>}

      <div className="spec-strip" style={{ marginBottom: 22 }}>
        <span className="spec"><span className="k">Unit</span><span className="v">{product.metafields.unit}</span></span>
        <span className="spec"><span className="k">Min</span><span className="v">{min}</span></span>
        <span className="spec"><span className="k">Mult</span><span className="v">×{step}</span></span>
        {product.metafields.backorderable && <span className="tag-outline">Backorderable</span>}
      </div>

      <div className="foot" style={{ display: 'flex', alignItems: 'center', gap: 14 }}>
        <div className="qty-wrap">
          <div className="qty">
            <button onClick={() => setQty((q) => clamp(q - step))} aria-label="Decrease quantity">
              <MinusIcon size={16} />
            </button>
            <input value={qty} readOnly tabIndex={-1} aria-label={`Quantity for ${product.title}`} />
            <button onClick={() => setQty((q) => clamp(q + step))} aria-label="Increase quantity">
              <PlusIcon size={16} />
            </button>
          </div>
          {step > 1 && <span className={`qty-note${offStep ? ' warn' : ''}`}>multiples of {step}</span>}
          <span className="sr-only" aria-live="polite">Quantity {qty}{offStep ? `, not a multiple of ${step}` : ''}</span>
        </div>
        <button className={`btn btn-primary btn-lg${added ? ' added' : ''}`} onClick={add} disabled={adding}>
          {adding ? <Spinner /> : added ? <><CheckIcon size={16} /> Added</> : 'Add to cart'}
        </button>
      </div>

      <p className="muted" style={{ fontSize: 13, marginTop: 16 }}>
        Availability shown is advisory — your contract price and live stock are confirmed at the checkout gate.
      </p>
    </div>
  )
}
