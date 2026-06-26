import { useEffect, useState } from 'react'
import { getCatalog, addToCart, type CatalogProduct } from '../api'

export function Catalog() {
  const [products, setProducts] = useState<CatalogProduct[]>([])
  const [error, setError] = useState<string>()

  useEffect(() => {
    getCatalog()
      .then(setProducts)
      .catch((e: Error) => setError(e.message))
  }, [])

  return (
    <section>
      <h1>Catalog</h1>
      {error && <p style={{ color: 'crimson' }}>{error}</p>}
      <ul>
        {products.map((p) => (
          <li key={p.sku} style={{ marginBottom: 8 }}>
            <strong>{p.title}</strong> ({p.sku}) — {p.productType}{' '}
            <button onClick={() => addToCart({ itemNumber: p.sku, quantity: p.metafields.minOrderQty || 1, site: '1' })}>
              Add to cart
            </button>
          </li>
        ))}
      </ul>
    </section>
  )
}
