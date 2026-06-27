import { useEffect, useMemo, useState } from 'react'
import { getCatalog, addToCart, type CatalogProduct } from '../api'
import { Banner, EmptyState, Eyebrow, Loading, Spinner, CheckIcon } from '../components/ui'
import { ProductMedia } from '../components/ProductMedia'
import { MinusIcon, PlusIcon } from '../components/icons'
import { useCart } from '../context/cart'
import { formatMoney } from '../format'

function ProductCard({ product }: { product: CatalogProduct }) {
  const min = product.metafields.minOrderQty || 1
  const step = product.metafields.orderMultiple || 1
  const [qty, setQty] = useState(min)
  const [adding, setAdding] = useState(false)
  const [added, setAdded] = useState(false)
  const { applyCart } = useCart()

  // Reachable quantities are min, min+step, min+2·step… — anchor the clamp to min (not zero) and
  // use integer step counts so a non-multiple minOrderQty and fractional steps stay exact.
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
      setTimeout(() => setAdded(false), 1400)
    } finally {
      setAdding(false)
    }
  }

  return (
    <article className="product">
      <ProductMedia productType={product.productType} sku={product.sku} />
      <div className="body">
        <Eyebrow>{product.productType}</Eyebrow>
        <h3 className="title">{product.title}</h3>
        <span className="sku">{product.sku}</span>
        {product.listPrice != null ? (
          <div className="price">
            {formatMoney(product.listPrice)} <span className="price-tag">list / {product.metafields.unit}</span>
          </div>
        ) : (
          <div className="price muted" style={{ fontWeight: 400, fontSize: 13 }}>Priced at checkout</div>
        )}
        <div className="spec-strip">
          <span className="spec">
            <span className="k">Unit</span>
            <span className="v">{product.metafields.unit}</span>
          </span>
          <span className="spec">
            <span className="k">Min</span>
            <span className="v">{min}</span>
          </span>
          <span className="spec">
            <span className="k">Mult</span>
            <span className="v">×{step}</span>
          </span>
          {product.metafields.backorderable && <span className="tag-outline">Backorderable</span>}
        </div>
        <div className="foot">
          <div className="qty-wrap">
            <div className="qty">
              <button onClick={() => setQty((q) => clamp(q - step))} aria-label="Decrease quantity">
                <MinusIcon size={15} />
              </button>
              <input value={qty} readOnly tabIndex={-1} aria-label={`Quantity for ${product.title}`} />
              <button onClick={() => setQty((q) => clamp(q + step))} aria-label="Increase quantity">
                <PlusIcon size={15} />
              </button>
            </div>
            {step > 1 && <span className={`qty-note${offStep ? ' warn' : ''}`}>multiples of {step}</span>}
          </div>
          <button
            className={`btn btn-primary btn-sm${added ? ' added' : ''}`}
            style={{ marginLeft: 'auto' }}
            onClick={add}
            disabled={adding}
          >
            {adding ? <Spinner /> : added ? <><CheckIcon size={15} /> Added</> : 'Add'}
          </button>
        </div>
      </div>
    </article>
  )
}

type Sort = 'featured' | 'title' | 'category'

export function Catalog() {
  const [products, setProducts] = useState<CatalogProduct[]>()
  const [error, setError] = useState<string>()
  const [category, setCategory] = useState('All')
  const [sort, setSort] = useState<Sort>('featured')

  useEffect(() => {
    getCatalog()
      .then(setProducts)
      .catch((e: Error) => setError(e.message))
  }, [])

  const categories = useMemo(
    () => ['All', ...Array.from(new Set((products ?? []).map((p) => p.productType))).sort()],
    [products],
  )

  const visible = useMemo(() => {
    let list = (products ?? []).filter((p) => category === 'All' || p.productType === category)
    if (sort === 'title') list = [...list].sort((a, b) => a.title.localeCompare(b.title))
    if (sort === 'category') list = [...list].sort((a, b) => a.productType.localeCompare(b.productType) || a.title.localeCompare(b.title))
    return list
  }, [products, category, sort])

  return (
    <>
      <section className="hero">
        <div className="inner">
          <div>
            <Eyebrow accent>Parts Catalog</Eyebrow>
            <h1>Precision parts, ordered with confidence.</h1>
            <p className="sub">
              Browse the full range and build your order. Availability, contract pricing and credit are confirmed
              live at the checkout gate — so what you order is what we can fulfil.
            </p>
          </div>
          {products && (
            <div className="stats">
              <div className="stat">
                <div className="n tnum">{products.length}</div>
                <div className="l">SKUs</div>
              </div>
              <div className="stat">
                <div className="n tnum">{categories.length - 1}</div>
                <div className="l">Categories</div>
              </div>
              <div className="stat">
                <div className="n">Live</div>
                <div className="l">ATP check</div>
              </div>
            </div>
          )}
        </div>
      </section>

      {products && products.length > 0 && (
        <div className="toolbar">
          <div className="inner">
            <div className="chips">
              {categories.map((c) => (
                <button key={c} className={`chip${category === c ? ' active' : ''}`} onClick={() => setCategory(c)}>
                  {c}
                </button>
              ))}
            </div>
            <span className="spacer" />
            <label className="sort">
              <Eyebrow>Sort</Eyebrow>
              <select value={sort} onChange={(e) => setSort(e.target.value as Sort)} aria-label="Sort products">
                <option value="featured">Featured</option>
                <option value="title">Name (A–Z)</option>
                <option value="category">Category</option>
              </select>
            </label>
          </div>
        </div>
      )}

      <div className="container page">
        {error && <Banner kind="danger">Could not load the catalog: {error}</Banner>}
        {!products && !error && <Loading>Loading catalog…</Loading>}
        {products && products.length === 0 && <EmptyState title="No products yet">The catalog is empty.</EmptyState>}
        {products && products.length > 0 && (
          <>
            <h2 className="sr-only">Products</h2>
            <p className="result-count">
              Showing {visible.length} {visible.length === 1 ? 'part' : 'parts'}
              {category !== 'All' ? ` in ${category}` : ''}
            </p>
            <div className="grid">
              {visible.map((product) => (
                <ProductCard key={product.sku} product={product} />
              ))}
            </div>
          </>
        )}
      </div>
    </>
  )
}
