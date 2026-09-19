import { use } from 'react'

import { AuthContext, type AuthContextValue } from './context'

export function useAuth(): AuthContextValue {
  const context = use(AuthContext)

  if (context === null) {
    throw new Error('useAuth must be used inside <AuthProvider>.')
  }

  return context
}
