import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'

import { api, setUnauthorizedHandler, tokenStore, unwrap } from '@/shared/api/client'

import { AuthContext, type AuthContextValue } from './context'
import { toAuthenticatedUser, userStore, type AuthenticatedUser } from './types'

/**
 * Auth is the one piece of genuinely client-owned state in this app, so it lives in
 * context. Everything else — meters, readings, invoices — is server state and
 * belongs to TanStack Query. See docs/adr/0011 for why there is no Redux here.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthenticatedUser | null>(() =>
    // Only trust a stored session if the token is still present alongside it.
    tokenStore.get() ? userStore.get() : null,
  )

  const signOut = useCallback(() => {
    tokenStore.clear()
    userStore.clear()
    setUser(null)
  }, [])

  // The fetch middleware clears the token on any 401; this keeps the UI in step, so
  // an expired session becomes a redirect to the login page rather than a sequence
  // of failing requests behind a signed-in shell.
  useEffect(() => {
    setUnauthorizedHandler(signOut)
    return () => setUnauthorizedHandler(() => {})
  }, [signOut])

  const signIn = useCallback(async (email: string, password: string) => {
    const response = await unwrap(api.POST('/api/v1/auth/login', { body: { email, password } }))

    tokenStore.set(response.accessToken)
    const authenticated = toAuthenticatedUser(response)
    userStore.set(authenticated)
    setUser(authenticated)
    return authenticated
  }, [])

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      isAuthenticated: user !== null,
      isAdmin: user?.role === 'Admin',
      signIn,
      signOut,
    }),
    [user, signIn, signOut],
  )

  return <AuthContext value={value}>{children}</AuthContext>
}
