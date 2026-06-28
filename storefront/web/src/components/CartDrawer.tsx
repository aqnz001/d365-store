import { useEffect, useMemo, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { getCatalog, removeFromCart, type CatalogProduct } from '../api'
import { useCart } from '../context/cart'
import { ProductMedia } from './ProductMedia'
import { ArrowRight, TrashIcon } from './icons'
import { formatMoney } from '../format'

/** Slide-out cart drawer — opens from the header cart icon and on add-to-cart. */
export function CartDrawer() {
  const { cart, count, drawerOpen, closeDrawer, applyCart } = useCart()
  const [catalog, setCatalog] = useState<Record<string, CatalogProduct>>({})
  const panelRef = useRef<HTMLElement>(null)
  const closeRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    getCatalog()
      .then((products) => setCatalog(Object.fromEntries(products.map((p) => [p.sku, p]))))
      .catch(() => undefined)
  }, [])

  // Dialog contract while open: lock body scroll, move focus into the panel, trap Tab, close on
  // Escape, and restore focus to the trigger on close. (When closed the panel is `inert`.)
  useEffect(() => {
    if (!drawerOpen) return
    const trigger = document.activeElement as HTMLElement | null

    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        closeDrawer()
        return
      }
      if (e.key !== 'Tab' || !panelRef.current) return
      const focusables = panelRef.current.querySelectorAll<HTMLElement>(
        'a[href], button:not([disabled]), input, select, textarea, [tabindex]:not([tabindex="-1"])',
      )
      if (focusables.length === 0) return
      const first = focusables[0]
      const last = focusables[focusables.length - 1]
      if (e.shiftKey && document.activeElement === first) {
        e.preventDefault()
        last.focus()
      } else if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault()
        first.focus()
      }
    }

    document.addEventListener('keydown', onKey)
    const prevOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    const t = window.setTimeout(() => closeRef.current?.focus(), 0)

    return () => {
      document.removeEventListener('keydown', onKey)
      document.body.style.overflow = prevOverflow
      window.clearTimeout(t)
      trigger?.focus?.()
    }
  }, [drawerOpen, closeDrawer])

  const lines = cart?.lines ?? []
  const estimate = useMemo(() => {
    let total = 0
    let allPriced = lines.length > 0
    for (const line of lines) {
      const price = catalog[line.itemNumber]?.listPrice
      if (price == null) allPriced = false
      else total += price * line.quantity
    }
    return { total, allPriced }
  }, [lines, catalog])

  const remove = async (index: number) => {
    try {
      applyCart(await removeFromCart(index))
    } catch {
      // ignore — the cart page surfaces errors
    }
  }

  return (
    <>
      <div
        className={`drawer-overlay${drawerOpen ? ' open' : ''}`}
        style={{ pointerEvents: drawerOpen ? 'auto' : 'none' }}
        onClick={closeDrawer}
        aria-hidden="true"
      />
      <aside
        ref={panelRef}
        className={`drawer${drawerOpen ? ' open' : ''}`}
        role="dialog"
        aria-modal="true"
        aria-label="Shopping cart"
        inert={drawerOpen ? undefined : true}
      >
        <div className="drawer-head">
          <h2>Your cart{count > 0 ? ` (${count})` : ''}</h2>
          <button ref={closeRef} className="icon-btn" aria-label="Close cart" onClick={closeDrawer}>✕</button>
        </div>

        {lines.length === 0 ? (
          <div className="drawer-empty">
            <p>Your cart is empty.</p>
            <Link className="btn btn-primary" to="/" onClick={closeDrawer}>Browse the catalog</Link>
          </div>
        ) : (
          <>
            <div className="drawer-body">
              {lines.map((line, index) => {
                const product = catalog[line.itemNumber]
                return (
                  <div className="drawer-line" key={`${line.itemNumber}-${index}`}>
                    <ProductMedia productType={product?.productType ?? ''} sku={line.itemNumber} variant="thumb" />
                    <div className="grow">
                      <div className="dl-title">{product?.title ?? line.itemNumber}</div>
                      <div className="mono" style={{ color: 'var(--ink-2)', fontSize: 12, marginTop: 2 }}>{line.itemNumber}</div>
                      <div className="muted" style={{ fontSize: 13, marginTop: 6 }}>
                        Qty {line.quantity}
                        {product?.listPrice != null && <> · {formatMoney(product.listPrice * line.quantity)}</>}
                      </div>
                    </div>
                    <button className="btn-link" onClick={() => remove(index)} aria-label={`Remove ${product?.title ?? line.itemNumber}`}>
                      <TrashIcon size={16} />
                    </button>
                  </div>
                )
              })}
            </div>

            <div className="drawer-foot">
              <div className="row">
                <span className="muted">Estimated total</span>
                <span className="sub-total">{estimate.allPriced ? formatMoney(estimate.total) : '—'}</span>
              </div>
              <p className="muted" style={{ fontSize: 12 }}>List prices — contract price &amp; credit confirmed at checkout.</p>
              <Link className="btn btn-primary btn-block btn-lg" to="/checkout" onClick={closeDrawer}>
                Checkout <ArrowRight size={16} />
              </Link>
              <Link className="btn btn-block" to="/cart" onClick={closeDrawer}>View full cart</Link>
            </div>
          </>
        )}
      </aside>
    </>
  )
}
