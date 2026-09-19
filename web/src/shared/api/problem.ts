import type { SchemaHttpValidationProblemDetails, SchemaProblemDetails } from './schema'

/**
 * The API returns RFC 9457 problem documents for every failure. Normalising them
 * here means every screen renders errors the same way, and — more importantly —
 * that the backend's validation messages reach the user verbatim instead of being
 * replaced by a generic "something went wrong".
 */
export class ApiProblem extends Error {
  readonly status: number
  readonly title: string
  readonly detail: string | undefined
  /** Stable URI identifying the problem type. Branch on this, never on the message. */
  readonly type: string | undefined
  /** Per-field messages from FluentValidation, keyed by property name. */
  readonly errors: Record<string, string[]> | undefined
  readonly traceId: string | undefined

  constructor(init: {
    status: number
    title: string
    detail?: string | undefined
    type?: string | undefined
    errors?: Record<string, string[]> | undefined
    traceId?: string | undefined
  }) {
    super(init.detail ?? init.title)
    this.name = 'ApiProblem'
    this.status = init.status
    this.title = init.title
    this.detail = init.detail
    this.type = init.type
    this.errors = init.errors
    this.traceId = init.traceId
  }

  /** Field-level messages for a form, flattened to one string per field. */
  get fieldErrors(): Record<string, string> {
    if (!this.errors) return {}
    return Object.fromEntries(
      Object.entries(this.errors).map(([field, messages]) => [
        // The backend sends PascalCase property names; forms use camelCase.
        field.charAt(0).toLowerCase() + field.slice(1),
        messages.join(' '),
      ]),
    )
  }

  get isUnauthorized(): boolean {
    return this.status === 401
  }

  get isForbidden(): boolean {
    return this.status === 403
  }
}

/**
 * ProblemDetails carries an `extensions` bag that JSON Schema cannot describe, so
 * traceId (added by the API's CustomizeProblemDetails hook) is not in the generated
 * types. An index signature models that honestly rather than casting to `any`.
 */
type AnyProblem = SchemaProblemDetails &
  Partial<SchemaHttpValidationProblemDetails> & { [key: string]: unknown }

/**
 * Turns whatever came back into an ApiProblem. Falls back gracefully: a proxy
 * returning an HTML error page, or a network failure, must not crash the renderer
 * with "problem.title is undefined".
 */
export function toApiProblem(status: number, body: unknown): ApiProblem {
  const problem = (typeof body === 'object' && body !== null ? body : {}) as AnyProblem

  return new ApiProblem({
    status: problem.status ?? status,
    title: problem.title ?? defaultTitleFor(status),
    detail: problem.detail ?? undefined,
    type: problem.type ?? undefined,
    errors: problem.errors ?? undefined,
    traceId: typeof problem['traceId'] === 'string' ? problem['traceId'] : undefined,
  })
}

function defaultTitleFor(status: number): string {
  if (status === 0) return 'Cannot reach the server'
  if (status === 401) return 'Sign-in required'
  if (status === 403) return 'Access denied'
  if (status === 404) return 'Not found'
  if (status === 429) return 'Too many requests'
  if (status >= 500) return 'Server error'
  return 'Request failed'
}
