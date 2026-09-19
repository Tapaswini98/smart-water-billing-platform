import { Card } from './Card'

/**
 * An honest placeholder. A screen that is not built yet says so, with what it will
 * do and which endpoint it is waiting on — rather than showing fabricated data that
 * a reviewer might mistake for a working feature.
 */
export function PendingFeature({
  title,
  summary,
  waitingOn,
}: {
  title: string
  summary: string
  waitingOn: string[]
}) {
  return (
    <Card title={title} description="Not implemented yet">
      <p className="prose">{summary}</p>
      <p className="prose prose--muted">Waiting on these API endpoints:</p>
      <ul className="prose prose--muted">
        {waitingOn.map((endpoint) => (
          <li key={endpoint}>
            <code>{endpoint}</code>
          </li>
        ))}
      </ul>
    </Card>
  )
}
