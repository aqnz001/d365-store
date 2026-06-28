// Auth state for the SPA. Sign-in/up/reset pages are hosted by Entra External ID (DR-004); here we
// only detect the session (via the BFF /me) and gate the app behind it. The BFF holds the tokens
// server-side (Golden Rule #11); the SPA never sees them.
import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { probeAuth, type CurrentUser } from '../api'

type AuthState =
  | { status: 'loading' }
  | { status: 'authed'; user: CurrentUser }
  | { status: 'anon' }

const AuthContext = createContext<AuthState | undefined>(undefined)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({ status: 'loading' })

  useEffect(() => {
    let active = true
    probeAuth()
      .then((user) => {
        if (active) setState(user ? { status: 'authed', user } : { status: 'anon' })
      })
      .catch(() => {
        if (active) setState({ status: 'anon' })
      })
    return () => {
      active = false
    }
  }, [])

  return <AuthContext.Provider value={state}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthState {
  const context = useContext(AuthContext)
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider')
  }
  return context
}

/** The signed-in user, or undefined when not authenticated (use inside an authed subtree). */
export function useCurrentUser(): CurrentUser | undefined {
  const auth = useAuth()
  return auth.status === 'authed' ? auth.user : undefined
}
