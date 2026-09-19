import { Component, type ErrorInfo, type ReactNode } from 'react'

/**
 * Catches render-time crashes so a bug in one screen does not leave the user with a
 * blank page and no way forward. Network and API failures do NOT come here — those
 * are values, handled by ProblemAlert.
 */
export class ErrorBoundary extends Component<{ children: ReactNode }, { error: Error | null }> {
  override state: { error: Error | null } = { error: null }

  static getDerivedStateFromError(error: Error) {
    return { error }
  }

  override componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('Unhandled render error', error, info.componentStack)
  }

  override render() {
    if (this.state.error) {
      return (
        <div className="alert alert--danger" role="alert">
          <p className="alert__title">Something went wrong on this screen</p>
          <p className="alert__detail">{this.state.error.message}</p>
          <button className="btn btn--secondary" onClick={() => this.setState({ error: null })}>
            Try again
          </button>
        </div>
      )
    }

    return this.props.children
  }
}
