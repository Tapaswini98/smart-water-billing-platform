import type { ReactNode } from 'react'

export function Card({
  title,
  description,
  actions,
  children,
}: {
  title?: ReactNode
  description?: ReactNode
  actions?: ReactNode
  children: ReactNode
}) {
  return (
    <section className="card">
      {title !== undefined || actions !== undefined ? (
        <header className="card__header">
          <div>
            {title !== undefined ? <h2 className="card__title">{title}</h2> : null}
            {description !== undefined ? <p className="card__description">{description}</p> : null}
          </div>
          {actions !== undefined ? <div className="card__actions">{actions}</div> : null}
        </header>
      ) : null}
      <div className="card__body">{children}</div>
    </section>
  )
}
