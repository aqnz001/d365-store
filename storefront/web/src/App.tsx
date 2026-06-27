import { createBrowserRouter, NavLink, Outlet } from 'react-router-dom'
import { Catalog } from './pages/Catalog'
import { Cart } from './pages/Cart'
import { Checkout } from './pages/Checkout'
import { Account } from './pages/Account'

const navClass = ({ isActive }: { isActive: boolean }) => (isActive ? 'active' : '')

function Layout() {
  return (
    <>
      <header className="app-header">
        <div className="bar">
          <span className="brand">
            <span className="dot">P</span> Parts Portal
          </span>
          <nav>
            <NavLink to="/" end className={navClass}>
              Catalog
            </NavLink>
            <NavLink to="/cart" className={navClass}>
              Cart
            </NavLink>
            <NavLink to="/checkout" className={navClass}>
              Checkout
            </NavLink>
            <NavLink to="/account" className={navClass}>
              Account
            </NavLink>
          </nav>
        </div>
      </header>
      <div className="container">
        <Outlet />
      </div>
      <footer>B2B parts ordering — availability and pricing are confirmed live at the checkout gate.</footer>
    </>
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
