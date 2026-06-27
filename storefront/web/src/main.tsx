import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RouterProvider } from 'react-router-dom'
import { router } from './App'
import { CartProvider } from './context/cart'
import './index.css'

// Dev safety net: this app ships no service worker, but localhost ports get reused across projects
// and a stale SW from a previous app on :5173 will intercept requests and 404 them. Unregister any
// such worker on load so the dev server isn't shadowed. (Once it's controlling the page you still
// need one manual "Unregister" in DevTools → Application → Service workers to break the first load.)
if (import.meta.env.DEV && 'serviceWorker' in navigator) {
  navigator.serviceWorker.getRegistrations().then((registrations) => {
    registrations.forEach((registration) => void registration.unregister())
  })
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <CartProvider>
      <RouterProvider router={router} />
    </CartProvider>
  </StrictMode>,
)
