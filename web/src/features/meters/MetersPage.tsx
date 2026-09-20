import { useState } from 'react'
import { Link } from 'react-router-dom'

import { useAuth } from '@/features/auth/useAuth'
import { formatDateTime, formatVolume } from '@/shared/lib/format'
import { Badge } from '@/shared/ui/Badge'
import { Button } from '@/shared/ui/Button'
import { Card } from '@/shared/ui/Card'
import { EmptyState } from '@/shared/ui/EmptyState'
import { ProblemAlert } from '@/shared/ui/ProblemAlert'
import { Spinner } from '@/shared/ui/Spinner'

import { useMeters } from './queries'

const PAGE_SIZE = 25

export function MetersPage() {
  const { isAdmin } = useAuth()
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')

  const { data, isPending, isError, error, isFetching } = useMeters({
    page,
    pageSize: PAGE_SIZE,
    search: search.trim() || undefined,
  })

  if (isPending) {
    return (
      <Card title="Meters">
        <Spinner label="Loading meters" />
      </Card>
    )
  }

  if (isError) {
    return (
      <Card title="Meters">
        <ProblemAlert error={error} />
      </Card>
    )
  }

  const meters = data.meters ?? []
  const totalCount = data.totalCount ?? 0
  const lastPage = Math.max(1, Math.ceil(totalCount / PAGE_SIZE))

  return (
    <Card
      title="Meters"
      description={
        isAdmin
          ? `${totalCount} meter${totalCount === 1 ? '' : 's'} across the estate`
          : `${totalCount} meter${totalCount === 1 ? '' : 's'} assigned to your account`
      }
      actions={isFetching ? <Spinner label="Refreshing" /> : null}
    >
      <div className="inline-form">
        <label className="field field--grow">
          <span className="field__label">Search by serial or location</span>
          <input
            className="field__input"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value)
              setPage(1)
            }}
            placeholder="WM-2024"
          />
        </label>
      </div>

      {meters.length === 0 ? (
        <EmptyState
          title="No meters found"
          description={search ? 'No meter matches that search.' : 'No meters are registered yet.'}
        />
      ) : (
        <>
          <div className="table-scroll">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">Serial</th>
                  <th scope="col">Location</th>
                  {isAdmin ? <th scope="col">Customer</th> : null}
                  <th scope="col">Tariff</th>
                  <th scope="col">Status</th>
                  <th scope="col" className="table__num">
                    Latest total
                  </th>
                  <th scope="col">Last reading</th>
                </tr>
              </thead>
              <tbody>
                {meters.map((meter) => (
                  <tr key={meter.id}>
                    <td>
                      <Link to={`/meters/${meter.id}`}>{meter.serialNumber}</Link>
                    </td>
                    <td className="table__muted">{meter.locationDescription ?? '—'}</td>
                    {isAdmin ? (
                      <td>
                        {meter.customerName ?? (
                          // Surfaced rather than hidden: an unassigned meter is
                          // skipped by every billing run until someone acts on it.
                          <Badge tone="warn">Unassigned</Badge>
                        )}
                      </td>
                    ) : null}
                    <td className="table__muted">{meter.pricingPlanName ?? '—'}</td>
                    <td>
                      <Badge tone={statusTone(meter.status)}>{meter.status}</Badge>
                      {meter.desiredSupplyState === 'Closed' ? (
                        <Badge tone="danger">Supply closed</Badge>
                      ) : null}
                    </td>
                    <td className="table__num">{formatVolume(meter.latestTotalM3)}</td>
                    <td className="table__muted">{formatDateTime(meter.lastReadingAtUtc)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {lastPage > 1 ? (
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
          ) : null}
        </>
      )}
    </Card>
  )
}

function statusTone(status: string | undefined): 'success' | 'info' | 'warn' | 'neutral' {
  switch (status) {
    case 'Active':
      return 'success'
    case 'Provisioned':
      return 'info'
    case 'Suspended':
      return 'warn'
    default:
      return 'neutral'
  }
}
