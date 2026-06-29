import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { searchCatalog, addToCart, type CatalogPage, type CatalogProduct } from '../api'
import { Banner, BandBadge, EmptyState, Eyebrow, Loading, Spinner, CheckIcon } from '../components/ui'
import { ProductMedia } from '../components/ProductMedia'
import { MinusIcon, PlusIcon, SearchIcon } from '../components/icons'
import { useCart } from '../context/cart'
import { formatMoney } from '../format'

const PAGE_SIZE = 12

function ProductCard({ product }: { product: CatalogProduct }) {
  const min = product.metafields.minOrderQty || 1
  const step = product.metafields.orderMultiple || 1
  const [qty, setQty] = useState(min)
  const [adding, setAdding] = useState(false)
  const [added, setAdded] = useState(false)
  const { applyCart, openDrawer } = useCart()

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
      openDrawer()
      setTimeout(() => setAdded(false), 1400)
    } finally {
      setAdding(false)
    }
  }

  const href = `/product/${encodeURIComponent(product.sku)}`

  return (
    <article className="product">
      <Link to={href} className="media-link" aria-label={product.title}>
        <ProductMedia productType={product.productType} sku={product.sku}>
          {product.availabilityBand && <BandBadge band={product.availabilityBand} onMedia />}
        </ProductMedia>
      </Link>
      <div className="body">
        <Eyebrow>{product.productType}</Eyebrow>
        <h3 className="title"><Link to={href} className="title-link">{product.title}</Link></h3>
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
            <span className="sr-only" aria-live="polite">
              Quantity {qty}{offStep ? `, not a multiple of ${step}` : ''}
            </span>
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
  const [data, setData] = useState<CatalogPage>()
  const [error, setError] = useState<string>()
  const [loading, setLoading] = useState(false)
  const [category, setCategory] = useState('All')
  const [sort, setSort] = useState<Sort>('featured')
  const [query, setQuery] = useState('') // search input
  const [debouncedQuery, setDebouncedQuery] = useState('')
  const [page, setPage] = useState(1)

  // Debounce the search box → one server query per pause, resetting to the first page.
  useEffect(() => {
    const id = setTimeout(() => {
      setDebouncedQuery(query.trim())
      setPage(1)
    }, 300)
    return () => clearTimeout(id)
  }, [query])

  // Fetch the current page from the server (search/filter/sort/pagination all happen server-side).
  useEffect(() => {
    let ignore = false
    setLoading(true)
    searchCatalog({ q: debouncedQuery, category, sort, page, pageSize: PAGE_SIZE })
      .then((d) => {
        if (!ignore) {
          setData(d)
          setError(undefined)
        }
      })
      .catch((e: Error) => {
        if (!ignore) setError(e.message)
      })
      .finally(() => {
        if (!ignore) setLoading(false)
      })
    return () => {
      ignore = true
    }
  }, [debouncedQuery, category, sort, page])

  const changeCategory = (c: string) => {
    setCategory(c)
    setPage(1)
  }
  const changeSort = (s: Sort) => {
    setSort(s)
    setPage(1)
  }

  const categories = ['All', ...(data?.categories ?? [])]
  const items = data?.items ?? []
  const total = data?.total ?? 0
  // Use the page size the server actually applied (it clamps), not just the requested constant.
  const effectivePageSize = data?.pageSize ?? PAGE_SIZE
  const totalPages = Math.max(1, Math.ceil(total / effectivePageSize))
  const firstLoad = !data && !error
  const showToolbar = data !== undefined && (categories.length > 1 || debouncedQuery !== '')

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
          {data && (
            <div className="stats">
              <div className="stat">
                <div className="n tnum">{total}</div>
                <div className="l">{debouncedQuery || category !== 'All' ? 'Matches' : 'SKUs'}</div>
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

      {showToolbar && (
        <div className="toolbar">
          <div className="inner">
            <label className="search">
              <SearchIcon size={17} />
              <input
                type="search"
                placeholder="Search parts or SKU…"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                aria-label="Search the catalog"
              />
            </label>
            <div className="chips" role="group" aria-label="Filter by category">
              {categories.map((c) => (
                <button
                  key={c}
                  className={`chip${category === c ? ' active' : ''}`}
                  aria-pressed={category === c}
                  onClick={() => changeCategory(c)}
                >
                  {c}
                </button>
              ))}
            </div>
            <span className="spacer" />
            <label className="sort">
              <Eyebrow>Sort</Eyebrow>
              <select value={sort} onChange={(e) => changeSort(e.target.value as Sort)} aria-label="Sort products">
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
        {firstLoad && <Loading>Loading catalog…</Loading>}
        {data && (
          <>
            <h2 className="sr-only">Products</h2>
            <p className="result-count" aria-live="polite">
              Showing {items.length === 0 ? 0 : (page - 1) * effectivePageSize + 1}
              {items.length > 1 ? `–${(page - 1) * effectivePageSize + items.length}` : ''} of {total}{' '}
              {total === 1 ? 'part' : 'parts'}
              {category !== 'All' ? ` in ${category}` : ''}
              {debouncedQuery ? ` matching “${debouncedQuery}”` : ''}
            </p>
            {items.length === 0 ? (
              <EmptyState title="No parts found">Try a different search or category.</EmptyState>
            ) : (
              <>
                <div className={`grid${loading ? ' is-loading' : ''}`} aria-busy={loading}>
                  {items.map((product) => (
                    <ProductCard key={product.sku} product={product} />
                  ))}
                </div>
                {totalPages > 1 && (
                  <nav className="pagination" aria-label="Catalog pages">
                    <button
                      className="btn btn-sm"
                      onClick={() => setPage((p) => Math.max(1, p - 1))}
                      disabled={page <= 1 || loading}
                    >
                      ← Prev
                    </button>
                    <span className="page-of" aria-current="page">Page {page} of {totalPages}</span>
                    <button
                      className="btn btn-sm"
                      onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                      disabled={page >= totalPages || loading}
                    >
                      Next →
                    </button>
                  </nav>
                )}
              </>
            )}
          </>
        )}
      </div>
    </>
  )
}
