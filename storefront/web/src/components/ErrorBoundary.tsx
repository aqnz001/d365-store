import { Component, type ErrorInfo, type ReactNode } from 'react'

interface State {
  error: Error | null
}

/**
 * Top-level error boundary: a render error anywhere in the tree (including the auth gate and the
 * cart provider, which sit outside the router's own errorElement) shows a recoverable fallback
 * instead of an unhandled blank white screen.
 */
export class ErrorBoundary extends Component<{ children: ReactNode }, State> {
  state: State = { error: null }

  static getDerivedStateFromError(error: Error): State {
    return { error }
  }

  componentDidCatch(error: Error, info: ErrorInfo): void {
    // Surface to the console (and, in production, a real telemetry sink) for diagnosis.
    console.error('Unhandled UI error', error, info.componentStack)
  }

  render(): ReactNode {
    if (this.state.error) {
      return (
        <div className="app">
          <div className="container page">
            <div className="empty">
              <h2>Something went wrong</h2>
              <p className="muted">An unexpected error occurred. Reloading the page usually fixes it.</p>
              <p style={{ marginTop: 16 }}>
                <a className="btn btn-primary" href="/">Reload the storefront</a>
              </p>
            </div>
          </div>
        </div>
      )
    }

    return this.props.children
  }
}
