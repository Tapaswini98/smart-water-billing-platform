import type { ReactNode } from 'react'

export function EmptyState({
  title,
  description,
  action,
}: {
  title: string
  description?: ReactNode
  action?: ReactNode
}) {
  return (
    <div className="empty">
      <p className="empty__title">{title}</p>
      {description !== undefined ? <p className="empty__description">{description}</p> : null}
      {action !== undefined ? <div className="empty__action">{action}</div> : null}
    </div>
  )
}
