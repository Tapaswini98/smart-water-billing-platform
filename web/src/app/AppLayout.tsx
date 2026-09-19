import { NavLink, Outlet } from 'react-router-dom'

import { useAuth } from '@/features/auth/useAuth'
import { Button } from '@/shared/ui/Button'
import { ErrorBoundary } from '@/shared/ui/ErrorBoundary'

export function AppLayout() {
  const { user, isAdmin, signOut } = useAuth()

  return (
    <div className="shell">
      <header className="shell__header">
        <div className="shell__brand">
          <span className="shell__mark" aria-hidden="true">
            ◍
          </span>
          <span className="shell__name">Smart Water Billing</span>
        </div>

        <nav className="shell__nav" aria-label="Main">
          <NavLink to="/meters">Meters</NavLink>
          <NavLink to="/invoices">Invoices</NavLink>
          {/* Hidden for customers because the API would refuse it anyway — the menu
              reflects what the server permits rather than contradicting it. */}
          {isAdmin ? <NavLink to="/pricing">Pricing</NavLink> : null}
        </nav>

        <div className="shell__user">
          <span className="shell__username">
            {user?.fullName}
            <span className="shell__role">{user?.role}</span>
          </span>
          <Button variant="ghost" onClick={signOut}>
            Sign out
          </Button>
        </div>
      </header>

      <main className="shell__main">
        <ErrorBoundary>
          <Outlet />
        </ErrorBoundary>
      </main>
    </div>
  )
}
