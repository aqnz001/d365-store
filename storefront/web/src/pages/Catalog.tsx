import { useEffect, useState } from 'react'
import { getCatalog, addToCart, type CatalogProduct } from '../api'
import { Spinner, Banner, EmptyState } from '../components/ui'

function ProductCard({ product }: { product: CatalogProduct }) {
  const min = product.metafields.minOrderQty || 1
  const step = product.metafields.orderMultiple || 1
  const [qty, setQty] = useState(min)
  const [adding, setAdding] = useState(false)
  const [added, setAdded] = useState(false)

  const clamp = (value: number) => Math.max(min, Math.round(value / step) * step || step)

  const add = async () => {
    setAdding(true)
    setAdded(false)
    try {
      await addToCart({ itemNumber: product.sku, quantity: qty, site: '1' })
      setAdded(true)
    } finally {
      setAdding(false)
    }
  }

  return (
    <div className="card">
      <span className="cat">{product.productType}</span>
      <p className="title">{product.title}</p>
      <span className="sku">{product.sku}</span>
      <span className="meta">
        Unit {product.metafields.unit} · min {min} · multiples of {step}
        {product.metafields.backorderable ? ' · backorderable' : ''}
      </span>
      <div className="foot">
        <div className="qty">
          <button onClick={() => setQty((q) => clamp(q - step))} aria-label="Decrease quantity">−</button>
          <input value={qty} readOnly aria-label="Quantity" />
          <button onClick={() => setQty((q) => clamp(q + step))} aria-label="Increase quantity">+</button>
        </div>
        <button className="btn btn-primary btn-sm" onClick={add} disabled={adding}>
          {adding ? <Spinner /> : added ? 'Added' : 'Add to cart'}
        </button>
      </div>
    </div>
  )
}

export function Catalog() {
  const [products, setProducts] = useState<CatalogProduct[]>()
  const [error, setError] = useState<string>()

  useEffect(() => {
    getCatalog()
      .then(setProducts)
      .catch((e: Error) => setError(e.message))
  }, [])

  return (
    <section>
      <div className="page-head">
        <h1>Catalog</h1>
        <p className="muted">Browse parts and add them to your order. Availability is confirmed live at checkout.</p>
      </div>
      {error && <Banner kind="danger">Could not load the catalog: {error}</Banner>}
      {!products && !error && (
        <p className="muted">
          <Spinner /> Loading…
        </p>
      )}
      {products && products.length === 0 && <EmptyState>No products available.</EmptyState>}
      {products && products.length > 0 && (
        <div className="grid">
          {products.map((product) => (
            <ProductCard key={product.sku} product={product} />
          ))}
        </div>
      )}
    </section>
  )
}
