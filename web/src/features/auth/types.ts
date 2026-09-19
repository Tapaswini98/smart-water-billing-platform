import type { SchemaLoginResponse } from '@/shared/api/schema'

export type UserRole = 'Admin' | 'Customer'

export interface AuthenticatedUser {
  userId: string
  email: string
  fullName: string
  role: UserRole
  expiresAtUtc: string
}

const SESSION_STORAGE_KEY = 'waterbilling.user'

export function toAuthenticatedUser(response: SchemaLoginResponse): AuthenticatedUser {
  return {
    userId: response.userId,
    email: response.email,
    fullName: response.fullName,
    // The role claim is authoritative on the server; this copy only drives what the
    // UI offers. Every protected endpoint re-checks it, so a tampered value here
    // buys nothing beyond a confusing menu.
    role: response.role === 'Admin' ? 'Admin' : 'Customer',
    expiresAtUtc: response.expiresAtUtc,
  }
}

export const userStore = {
  get(): AuthenticatedUser | null {
    try {
      const raw = sessionStorage.getItem(SESSION_STORAGE_KEY)
      if (!raw) return null
      const user = JSON.parse(raw) as AuthenticatedUser
      // A stored session whose token has already expired is worse than none: it
      // renders a signed-in shell that 401s on first use.
      if (new Date(user.expiresAtUtc).getTime() <= Date.now()) return null
      return user
    } catch {
      return null
    }
  },
  set(user: AuthenticatedUser): void {
    try {
      sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(user))
    } catch {
      /* ignore */
    }
  },
  clear(): void {
    try {
      sessionStorage.removeItem(SESSION_STORAGE_KEY)
    } catch {
      /* ignore */
    }
  },
}
