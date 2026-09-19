import { createContext } from 'react'

import type { AuthenticatedUser } from './types'

export interface AuthContextValue {
  user: AuthenticatedUser | null
  isAuthenticated: boolean
  isAdmin: boolean
  signIn: (email: string, password: string) => Promise<AuthenticatedUser>
  signOut: () => void
}

/**
 * Kept in its own module, with no component exported alongside it, so that editing
 * the provider does not force React Fast Refresh to remount the whole tree — which
 * would sign the developer out on every save.
 */
export const AuthContext = createContext<AuthContextValue | null>(null)
