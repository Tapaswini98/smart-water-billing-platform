import type { ReactNode } from 'react'

export function Badge({
  tone = 'neutral',
  children,
}: {
  tone?: 'neutral' | 'info' | 'warn' | 'danger' | 'success'
  children: ReactNode
}) {
  return <span className={`badge badge--${tone}`}>{children}</span>
}
