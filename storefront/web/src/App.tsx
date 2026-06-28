import { useEffect, useRef, useState } from 'react'
import { createBrowserRouter, Link, NavLink, Outlet } from 'react-router-dom'
import { Catalog } from './pages/Catalog'
import { Cart } from './pages/Cart'
import { Checkout } from './pages/Checkout'
import { Account } from './pages/Account'
import { useCart } from './context/cart'
import { getMe } from './api'
import { BoltIcon, CartIcon, MenuIcon } from './components/icons'
import { CartDrawer } from './components/CartDrawer'

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
  const [scrolled, setScrolled] = useState(false)
  const [account, setAccount] = useState<string>()
  const [bump, setBump] = useState(false)
  const [menuOpen, setMenuOpen] = useState(false)
  const prevCount = useRef(count)

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 8)
    onScroll()
    window.addEventListener('scroll', onScroll, { passive: true })
    return () => window.removeEventListener('scroll', onScroll)
  }, [])

  useEffect(() => {
    getMe()
      .then((m) => setAccount(m.customerAccount))
      .catch(() => undefined)
  }, [])

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
          <NavLink to="/account" className="icon-btn" aria-label="Account" title={account ?? 'Account'}>
            <span className="account-chip">{account ? initials(account) : '··'}</span>
          </NavLink>
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

function Layout() {
  return (
    <div className="app">
      <AnnouncementBar />
      <Header />
      <main>
        <Outlet />
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
    children: [
      { index: true, element: <Catalog /> },
      { path: 'cart', element: <Cart /> },
      { path: 'checkout', element: <Checkout /> },
      { path: 'account', element: <Account /> },
    ],
  },
])
