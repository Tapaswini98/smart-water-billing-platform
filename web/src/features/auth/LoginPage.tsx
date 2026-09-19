import { useState, type FormEvent } from 'react'
import { useLocation, useNavigate, Navigate } from 'react-router-dom'

import { ApiProblem } from '@/shared/api/problem'
import { Button } from '@/shared/ui/Button'
import { ProblemAlert } from '@/shared/ui/ProblemAlert'

import { useAuth } from './useAuth'

export function LoginPage() {
  const { signIn, isAuthenticated } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<unknown>(null)
  const [submitting, setSubmitting] = useState(false)

  if (isAuthenticated) {
    return <Navigate to="/" replace />
  }

  const fieldErrors = error instanceof ApiProblem ? error.fieldErrors : {}

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError(null)
    setSubmitting(true)

    try {
      await signIn(email, password)
      const from = (location.state as { from?: string } | null)?.from
      void navigate(from ?? '/', { replace: true })
    } catch (cause) {
      setError(cause)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <main className="auth">
      <form className="auth__card" onSubmit={(e) => void handleSubmit(e)} noValidate>
        <div className="auth__brand">
          <span className="auth__mark" aria-hidden="true">
            ◍
          </span>
          <div>
            <h1 className="auth__title">Smart Water Billing</h1>
            <p className="auth__subtitle">Sign in to your account</p>
          </div>
        </div>

        {/* Field-level messages come straight from the backend's FluentValidation
            rules, so the two layers never disagree about what "invalid" means. */}
        <ProblemAlert error={error} />

        <label className="field">
          <span className="field__label">Email</span>
          <input
            className="field__input"
            type="email"
            name="email"
            autoComplete="username"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            aria-invalid={fieldErrors['email'] !== undefined}
            required
          />
          {fieldErrors['email'] !== undefined ? (
            <span className="field__error">{fieldErrors['email']}</span>
          ) : null}
        </label>

        <label className="field">
          <span className="field__label">Password</span>
          <input
            className="field__input"
            type="password"
            name="password"
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            aria-invalid={fieldErrors['password'] !== undefined}
            required
          />
          {fieldErrors['password'] !== undefined ? (
            <span className="field__error">{fieldErrors['password']}</span>
          ) : null}
        </label>

        <Button type="submit" loading={submitting}>
          {submitting ? 'Signing in…' : 'Sign in'}
        </Button>

        <details className="auth__demo">
          <summary>Demo credentials</summary>
          <p>
            Admin: <code>admin@waterworks.local</code> / <code>Admin#12345</code>
            <br />
            Customer: <code>asha@example.com</code> / <code>Customer#12345</code>
          </p>
          <p className="auth__demo-note">
            Seeded in Development only. Meters authenticate separately, with a per-meter
            key rather than a sign-in.
          </p>
        </details>
      </form>
    </main>
  )
}
