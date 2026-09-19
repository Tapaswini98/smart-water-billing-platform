import { ApiProblem } from '@/shared/api/problem'

/**
 * Anything can be thrown in JavaScript, including objects with no useful string
 * form. Producing "[object Object]" in an error banner helps nobody, so non-Error
 * values are serialised deliberately.
 */
function describeUnknownError(error: unknown): string {
  if (error instanceof Error) return error.message
  if (typeof error === 'string') return error
  try {
    return JSON.stringify(error) ?? 'An unexpected error occurred.'
  } catch {
    return 'An unexpected error occurred.'
  }
}

/**
 * Renders an RFC 9457 problem document. Field-level validation errors are listed
 * individually, because "One or more validation errors occurred" on its own tells
 * the user nothing — the useful content is in the per-field messages the backend
 * already went to the trouble of producing.
 */
export function ProblemAlert({ error }: { error: unknown }) {
  if (error === null || error === undefined) return null

  const problem =
    error instanceof ApiProblem
      ? error
      : new ApiProblem({
          status: 0,
          title: 'Unexpected error',
          detail: describeUnknownError(error),
        })

  const fieldErrors = Object.entries(problem.fieldErrors)

  return (
    <div className="alert alert--danger" role="alert">
      <p className="alert__title">{problem.title}</p>
      {problem.detail !== undefined && problem.detail !== problem.title ? (
        <p className="alert__detail">{problem.detail}</p>
      ) : null}

      {fieldErrors.length > 0 ? (
        <ul className="alert__list">
          {fieldErrors.map(([field, message]) => (
            <li key={field}>
              <strong>{field}</strong>: {message}
            </li>
          ))}
        </ul>
      ) : null}

      {problem.traceId !== undefined ? (
        <p className="alert__trace">
          Trace ID <code>{problem.traceId}</code> — quote this when reporting the problem.
        </p>
      ) : null}
    </div>
  )
}
