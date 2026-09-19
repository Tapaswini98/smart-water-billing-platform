import createClient, { type Middleware } from 'openapi-fetch'

import { ApiProblem, toApiProblem } from './problem'
import type { paths } from './schema'

/**
 * Relative by design — see the comment in vite.config.ts. Vite would inline an
 * absolute URL at build time, which is how a single image ends up hard-coded to one
 * environment. In development Vite proxies /api; in production nginx does.
 */
const BASE_URL = '/api'

const TOKEN_STORAGE_KEY = 'waterbilling.accessToken'

/**
 * sessionStorage rather than localStorage: the token dies with the tab, which is a
 * meaningful reduction in exposure on the shared workstations a utility office
 * actually uses. Neither is as good as an httpOnly cookie — recorded as a known
 * limitation in the frontend README rather than quietly ignored.
 */
export const tokenStore = {
  get(): string | null {
    try {
      return sessionStorage.getItem(TOKEN_STORAGE_KEY)
    } catch {
      return null
    }
  },
  set(token: string): void {
    try {
      sessionStorage.setItem(TOKEN_STORAGE_KEY, token)
    } catch {
      /* Private browsing with storage blocked: the session simply won't survive a reload. */
    }
  },
  clear(): void {
    try {
      sessionStorage.removeItem(TOKEN_STORAGE_KEY)
    } catch {
      /* ignore */
    }
  },
}

/** Notified when the API rejects our token, so the auth context can sign the user out. */
type UnauthorizedHandler = () => void
let onUnauthorized: UnauthorizedHandler = () => {}

export function setUnauthorizedHandler(handler: UnauthorizedHandler): void {
  onUnauthorized = handler
}

const authMiddleware: Middleware = {
  onRequest({ request }) {
    const token = tokenStore.get()
    if (token) {
      request.headers.set('Authorization', `Bearer ${token}`)
    }
    return request
  },
  onResponse({ response }) {
    // A 401 on any request means the token is gone or expired. Clearing it here,
    // once, is why no individual screen has to think about session expiry.
    if (response.status === 401) {
      tokenStore.clear()
      onUnauthorized()
    }
    return response
  },
}

export const api = createClient<paths>({ baseUrl: BASE_URL })
api.use(authMiddleware)

/**
 * Unwraps an openapi-fetch result into the value, or throws an ApiProblem.
 *
 * TanStack Query expects a promise that resolves or rejects; openapi-fetch returns
 * `{ data, error }`. This adapter is the single place those two conventions meet,
 * so every query and mutation in the app has the same error semantics.
 */
export async function unwrap<TData>(
  call: Promise<{ data?: TData; error?: unknown; response: Response }>,
): Promise<TData> {
  let result: { data?: TData; error?: unknown; response: Response }

  try {
    result = await call
  } catch (cause) {
    // Network-level failure: the API is down, DNS failed, or the request was aborted.
    throw new ApiProblem({
      status: 0,
      title: 'Cannot reach the server',
      detail: cause instanceof Error ? cause.message : 'The request could not be sent.',
    })
  }

  if (result.error !== undefined || !result.response.ok) {
    throw toApiProblem(result.response.status, result.error)
  }

  return result.data as TData
}
