import { createBrowserRouter, Link, Outlet } from 'react-router-dom'
import { Catalog } from './pages/Catalog'
import { Cart } from './pages/Cart'
import { Checkout } from './pages/Checkout'
import { Account } from './pages/Account'

function Layout() {
  return (
    <div style={{ fontFamily: 'system-ui, sans-serif', maxWidth: 960, margin: '0 auto', padding: 16 }}>
      <header style={{ display: 'flex', gap: 16, borderBottom: '1px solid #ddd', paddingBottom: 12, marginBottom: 16 }}>
        <strong>Parts Portal</strong>
        <nav style={{ display: 'flex', gap: 12 }}>
          <Link to="/">Catalog</Link>
          <Link to="/cart">Cart</Link>
          <Link to="/checkout">Checkout</Link>
          <Link to="/account">Account</Link>
        </nav>
      </header>
      <main>
        <Outlet />
      </main>
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
