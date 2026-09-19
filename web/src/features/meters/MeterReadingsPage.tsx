import { useState } from 'react'
import { useParams, Link } from 'react-router-dom'

import { formatDateTime, formatFlow, formatVolume, humanizeFlag, parseFlags } from '@/shared/lib/format'
import { Badge } from '@/shared/ui/Badge'
import { Card } from '@/shared/ui/Card'
import { Button } from '@/shared/ui/Button'
import { EmptyState } from '@/shared/ui/EmptyState'
import { ProblemAlert } from '@/shared/ui/ProblemAlert'
import { Spinner } from '@/shared/ui/Spinner'

import { useMeterReadings } from './queries'

const PAGE_SIZE = 25

export function MeterReadingsPage() {
  const { meterId = '' } = useParams<{ meterId: string }>()
  const [page, setPage] = useState(1)

  const { data, isPending, isError, error, isFetching } = useMeterReadings(meterId, {
    page,
    pageSize: PAGE_SIZE,
  })

  if (isPending) {
    return (
      <Card title="Readings">
        <Spinner label="Loading readings" />
      </Card>
    )
  }

  if (isError) {
    return (
      <Card title="Readings">
        <ProblemAlert error={error} />
        <p className="prose prose--muted">
          <Link to="/meters">Back to meters</Link>
        </p>
      </Card>
    )
  }

  const readings = data.readings ?? []
  const totalCount = data.totalCount ?? 0
  const lastPage = Math.max(1, Math.ceil(totalCount / PAGE_SIZE))

  return (
    <Card
      title={data.serialNumber ?? 'Meter'}
      description={`${totalCount.toLocaleString()} reading${totalCount === 1 ? '' : 's'} recorded`}
      actions={isFetching ? <Spinner label="Refreshing" /> : null}
    >
      {readings.length === 0 ? (
        <EmptyState
          title="No readings yet"
          description="Readings appear here as soon as the meter posts them to the ingestion endpoint."
        />
      ) : (
        <>
          <div className="table-scroll">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">Reading taken</th>
                  <th scope="col" className="table__num">
                    Total
                  </th>
                  <th scope="col" className="table__num">
                    Flow
                  </th>
                  <th scope="col">Source</th>
                  <th scope="col">Flags</th>
                </tr>
              </thead>
              <tbody>
                {readings.map((reading) => {
                  const flags = parseFlags(reading.anomalies)
                  return (
                    <tr key={reading.id}>
                      <td>
                        {formatDateTime(reading.readingAtUtc)}
                        {/* Received-at differs from reading-at for buffered uploads;
                            showing both makes a store-and-forward device legible. */}
                        {reading.receivedAtUtc !== reading.readingAtUtc ? (
                          <span className="table__sub">
                            received {formatDateTime(reading.receivedAtUtc)}
                          </span>
                        ) : null}
                      </td>
                      <td className="table__num">{formatVolume(reading.totalM3)}</td>
                      <td className="table__num">{formatFlow(reading.flowM3PerHour)}</td>
                      <td>{reading.source}</td>
                      <td>
                        {flags.length === 0 ? (
                          <span className="table__muted">—</span>
                        ) : (
                          <div className="badge-row" title={reading.anomalyNotes ?? undefined}>
                            {flags.map((flag) => (
                              <Badge key={flag} tone={flag === 'TotalDecreased' ? 'danger' : 'warn'}>
                                {humanizeFlag(flag)}
                              </Badge>
                            ))}
                          </div>
                        )}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>

          <nav className="pager" aria-label="Pagination">
            <Button variant="secondary" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
              Previous
            </Button>
            <span className="pager__status">
              Page {page} of {lastPage}
            </span>
            <Button
              variant="secondary"
              disabled={page >= lastPage}
              onClick={() => setPage((p) => p + 1)}
            >
              Next
            </Button>
          </nav>
        </>
      )}
    </Card>
  )
}
