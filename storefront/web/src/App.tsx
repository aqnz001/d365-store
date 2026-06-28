import { lazy, Suspense, useEffect, useRef, useState } from 'react'
import { createBrowserRouter, Link, NavLink, Outlet, RouterProvider, useLocation } from 'react-router-dom'
// Routes are code-split so the initial bundle is just the shell + landing — Checkout (which pulls
// the Stripe SDK), Account, and the detail pages load on demand.
const Catalog = lazy(() => import('./pages/Catalog').then((m) => ({ default: m.Catalog })))
const Cart = lazy(() => import('./pages/Cart').then((m) => ({ default: m.Cart })))
const Checkout = lazy(() => import('./pages/Checkout').then((m) => ({ default: m.Checkout })))
const Account = lazy(() => import('./pages/Account').then((m) => ({ default: m.Account })))
const ProductDetail = lazy(() => import('./pages/ProductDetail').then((m) => ({ default: m.ProductDetail })))
const OrderDetail = lazy(() => import('./pages/OrderDetail').then((m) => ({ default: m.OrderDetail })))
import { CartProvider, useCart } from './context/cart'
import { useAuth, useCurrentUser } from './context/auth'
import { redirectToLogin } from './api'
import { BoltIcon, CartIcon, MenuIcon } from './components/icons'
import { CartDrawer } from './components/CartDrawer'
import { Spinner } from './components/ui'

const navClass = ({ isActive }: { isActive: boolean }) => `navlink${isActive ? ' active' : ''}`
const mNavClass = ({ isActive }: { isActive: boolean }) => (isActive ? 'active' : '')

function initials(account: string): string {
  const parts = account.split(/[-\s_]+/).filter(Boolean)
  const letters = parts.length >= 2 ? parts[0][0] + parts[1][0] : account.replace(/[^a-z0-9]/gi, '').slice(0, 2)
  return letters.toUpperCase() || 'AC'
}

function AnnouncementBar() {
  const [open, setOpen] = useState(true)
  if (!open) return null
  return (
    <div className="announce">
      <div className="inner">
        <span>Net-30 terms for approved accounts · Volume pricing on order multiples · Availability confirmed live at checkout</span>
        <button className="dismiss" aria-label="Dismiss announcement" onClick={() => setOpen(false)}>
          ✕
        </button>
      </div>
    </div>
  )
}

function Header() {
  const { count, openDrawer } = useCart()
  const user = useCurrentUser()
  const location = useLocation()
  const [scrolled, setScrolled] = useState(false)
  const [bump, setBump] = useState(false)
  const [menuOpen, setMenuOpen] = useState(false)
  const [acctOpen, setAcctOpen] = useState(false)
  const acctRef = useRef<HTMLDivElement>(null)
  const prevCount = useRef(count)
  const account = user?.customerAccount

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 8)
    onScroll()
    window.addEventListener('scroll', onScroll, { passive: true })
    return () => window.removeEventListener('scroll', onScroll)
  }, [])

  // Close the account menu on client-side navigation (a native <details> would stay open).
  useEffect(() => {
    setAcctOpen(false)
  }, [location.pathname])

  // While open, dismiss on Escape or a click/focus outside the menu.
  useEffect(() => {
    if (!acctOpen) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setAcctOpen(false)
    }
    const onPointer = (e: MouseEvent) => {
      if (acctRef.current && !acctRef.current.contains(e.target as Node)) setAcctOpen(false)
    }
    document.addEventListener('keydown', onKey)
    document.addEventListener('mousedown', onPointer)
    return () => {
      document.removeEventListener('keydown', onKey)
      document.removeEventListener('mousedown', onPointer)
    }
  }, [acctOpen])

  useEffect(() => {
    if (count > prevCount.current) {
      setBump(true)
      const t = setTimeout(() => setBump(false), 360)
      prevCount.current = count
      return () => clearTimeout(t)
    }
    prevCount.current = count
  }, [count])

  return (
    <header className={`header${scrolled ? ' scrolled' : ''}`}>
      <div className="bar">
        <Link to="/" className="wordmark" aria-label="Parts Portal home">
          <span className="glyph">
            <BoltIcon size={18} />
          </span>
          <span className="name">Parts</span>
        </Link>
        <nav aria-label="Primary">
          <NavLink to="/" end className={navClass}>
            Catalog
          </NavLink>
          <NavLink to="/account" className={navClass}>
            Orders
          </NavLink>
        </nav>
        <span className="spacer" />
        <div className="actions">
          <button
            className="icon-btn menu-btn"
            aria-label="Menu"
            aria-expanded={menuOpen}
            aria-controls="mobile-nav"
            onClick={() => setMenuOpen((o) => !o)}
          >
            <MenuIcon size={20} />
          </button>
          <div className="acct-menu" ref={acctRef}>
            <button
              type="button"
              className="icon-btn"
              aria-label="Account menu"
              aria-haspopup="menu"
              aria-expanded={acctOpen}
              title={account ?? 'Account'}
              onClick={() => setAcctOpen((o) => !o)}
            >
              <span className="account-chip">{account ? initials(account) : '··'}</span>
            </button>
            {acctOpen && (
              <div className="acct-panel" role="menu">
                {user && (
                  <div className="acct-id">
                    <div className="acct-name">{user.name ?? user.customerAccount}</div>
                    {user.email && <div className="acct-email">{user.email}</div>}
                  </div>
                )}
                <Link to="/account" className="acct-item" role="menuitem" onClick={() => setAcctOpen(false)}>
                  Account &amp; orders
                </Link>
                {/* POST (not a GET link) so a cross-site request can't force a logout. */}
                <form method="post" action="/api/auth/logout" className="acct-signout-form">
                  <button type="submit" className="acct-item acct-signout" role="menuitem">Sign out</button>
                </form>
              </div>
            )}
          </div>
          <button className="icon-btn" aria-label={`Cart, ${count} item${count === 1 ? '' : 's'}`} onClick={openDrawer}>
            <CartIcon size={22} />
            {count > 0 && (
              <span className={`cart-bubble${bump ? ' bump' : ''}`} aria-hidden="true">
                {count}
              </span>
            )}
          </button>
        </div>
      </div>
      {menuOpen && (
        <nav id="mobile-nav" className="mobile-nav" aria-label="Primary">
          <NavLink to="/" end className={mNavClass} onClick={() => setMenuOpen(false)}>
            Catalog
          </NavLink>
          <NavLink to="/cart" className={mNavClass} onClick={() => setMenuOpen(false)}>
            Cart
          </NavLink>
          <NavLink to="/checkout" className={mNavClass} onClick={() => setMenuOpen(false)}>
            Checkout
          </NavLink>
          <NavLink to="/account" className={mNavClass} onClick={() => setMenuOpen(false)}>
            Account
          </NavLink>
          <form method="post" action="/api/auth/logout" className="mnav-signout-form">
            <button type="submit" className="mnav-signout">Sign out</button>
          </form>
        </nav>
      )}
    </header>
  )
}

function Footer() {
  return (
    <footer className="footer">
      <div className="inner">
        <div className="cols">
          <div className="brand-col">
            <span className="name">
              <span className="glyph">
                <BoltIcon size={16} />
              </span>
              Parts
            </span>
            <p>B2B parts ordering with live availability, contract pricing, and net terms — built on Dynamics 365.</p>
          </div>
          <div className="col">
            <h4>Shop</h4>
            <Link to="/">Catalog</Link>
            <Link to="/cart">Cart</Link>
            <Link to="/checkout">Checkout</Link>
          </div>
          <div className="col">
            <h4>Account</h4>
            <Link to="/account">Order history</Link>
            <Link to="/account">Credit &amp; terms</Link>
          </div>
          <div className="col">
            <h4>Support</h4>
            <a href="#contact">Contact a rep</a>
            <a href="#returns">Returns</a>
            <a href="#terms">Terms</a>
          </div>
        </div>
        <div className="trust">
          <span>Secure checkout by Stripe · PCI SAQ-A</span>
          <span className="marks">
            <span className="mark">VISA</span>
            <span className="mark">MC</span>
            <span className="mark">AMEX</span>
          </span>
          <span>Net terms available for approved accounts</span>
        </div>
        <div className="bottom">
          <span>© {new Date().getFullYear()} Parts Portal</span>
          <span>Powered by live D365 availability</span>
        </div>
      </div>
    </footer>
  )
}

function NotFound() {
  return (
    <div className="container page">
      <div className="empty">
        <h2>Page not found</h2>
        <p className="muted">That link doesn’t exist or has moved.</p>
        <p style={{ marginTop: 16 }}>
          <Link className="btn btn-primary" to="/">Back to the catalog</Link>
        </p>
      </div>
    </div>
  )
}

function RouteError() {
  return (
    <div className="app">
      <div className="container page">
        <div className="empty">
          <h2>Something went wrong</h2>
          <p className="muted">An unexpected error occurred. Please try again.</p>
          <p style={{ marginTop: 16 }}>
            <a className="btn btn-primary" href="/">Reload the storefront</a>
          </p>
        </div>
      </div>
    </div>
  )
}

function RouteFallback() {
  return (
    <div className="container page" style={{ display: 'flex', justifyContent: 'center', padding: '80px 24px' }}>
      <Spinner />
    </div>
  )
}

function Layout() {
  return (
    <div className="app">
      <AnnouncementBar />
      <Header />
      <main>
        {/* One Suspense boundary for all lazily-loaded routes. */}
        <Suspense fallback={<RouteFallback />}>
          <Outlet />
        </Suspense>
      </main>
      <Footer />
      <CartDrawer />
    </div>
  )
}

export const router = createBrowserRouter([
  {
    path: '/',
    element: <Layout />,
    errorElement: <RouteError />,
    children: [
      { index: true, element: <Catalog /> },
      { path: 'product/:sku', element: <ProductDetail /> },
      { path: 'cart', element: <Cart /> },
      { path: 'checkout', element: <Checkout /> },
      { path: 'account', element: <Account /> },
      { path: 'account/orders/:reference', element: <OrderDetail /> },
      { path: '*', element: <NotFound /> },
    ],
  },
])

function SignInLanding() {
  return (
    <div className="signin">
      <div className="signin-card">
        <span className="glyph">
          <BoltIcon size={24} />
        </span>
        <h1>Parts Portal</h1>
        <p className="muted">Sign in to browse your contract catalog, place orders, and manage your account.</p>
        <button className="btn btn-primary btn-lg btn-block" onClick={redirectToLogin}>
          Sign in
        </button>
        <p className="muted" style={{ fontSize: 13, marginTop: 14 }}>Secured by Microsoft Entra.</p>
      </div>
    </div>
  )
}

function FullScreenLoader() {
  return (
    <div className="signin">
      <Spinner />
    </div>
  )
}

/** Top of the SPA: gate everything behind the session, then mount the cart + router. */
export function AppRoot() {
  const auth = useAuth()
  if (auth.status === 'loading') return <FullScreenLoader />
  if (auth.status === 'anon') return <SignInLanding />
  return (
    <CartProvider>
      <RouterProvider router={router} />
    </CartProvider>
  )
}
