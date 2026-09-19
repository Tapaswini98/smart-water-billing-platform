import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router-dom'

import { useAuth } from './useAuth'
import type { UserRole } from './types'

/**
 * Route guard. Note what this is NOT: a security boundary. It decides what the UI
 * offers; the API decides what the user can actually do. Both checks exist, and
 * only the server-side one matters for correctness.
 */
export function RequireAuth({ children, role }: { children: ReactNode; role?: UserRole }) {
  const { user } = useAuth()
  const location = useLocation()

  if (user === null) {
    // Remember where they were headed so sign-in can return them there.
    return <Navigate to="/login" replace state={{ from: location.pathname }} />
  }

  if (role !== undefined && user.role !== role) {
    return <Navigate to="/" replace />
  }

  return <>{children}</>
}
