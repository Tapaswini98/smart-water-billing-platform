import { Link, useParams } from 'react-router-dom'

import { formatDateTime, formatVolume, humanizeFlag } from '@/shared/lib/format'
import { Badge } from '@/shared/ui/Badge'
import { Card } from '@/shared/ui/Card'
import { EmptyState } from '@/shared/ui/EmptyState'
import { ProblemAlert } from '@/shared/ui/ProblemAlert'
import { Spinner } from '@/shared/ui/Spinner'

import { useMeter, useMonthlyConsumption } from './queries'

export function MeterDetailPage() {
  const { meterId = '' } = useParams<{ meterId: string }>()
  const meter = useMeter(meterId)
  const consumption = useMonthlyConsumption(meterId, 6)

  if (meter.isPending) {
    return (
      <Card title="Meter">
        <Spinner label="Loading meter" />
      </Card>
    )
  }

  if (meter.isError) {
    return (
      <Card title="Meter">
        <ProblemAlert error={meter.error} />
      </Card>
    )
  }

  const months = consumption.data?.months ?? []
  // Scale bars to the largest month so the shape of usage is readable even when the
  // absolute numbers are small.
  const peak = months.reduce((max, m) => Math.max(max, m.consumptionM3 ?? 0), 0)

  return (
    <div className="stack">
      <Card
        title={meter.data.serialNumber}
        description={meter.data.locationDescription ?? undefined}
        actions={<Link to={`/meters/${meterId}/readings`}>View readings →</Link>}
      >
        <dl className="detail-grid">
          <div>
            <dt>Status</dt>
            <dd>
              <Badge tone={meter.data.status === 'Active' ? 'success' : 'info'}>
                {meter.data.status}
              </Badge>
            </dd>
          </div>
          <div>
            <dt>Customer</dt>
            <dd>{meter.data.customerName ?? <Badge tone="warn">Unassigned</Badge>}</dd>
          </div>
          <div>
            <dt>Tariff</dt>
            <dd>{meter.data.pricingPlanName ?? '—'}</dd>
          </div>
          <div>
            <dt>Model</dt>
            <dd>{meter.data.model ?? '—'}</dd>
          </div>
          <div>
            <dt>Installed</dt>
            <dd>{formatDateTime(meter.data.installedAtUtc)}</dd>
          </div>
          <div>
            <dt>Latest total</dt>
            <dd>{formatVolume(meter.data.latestTotalM3)}</dd>
          </div>
          <div>
            <dt>Last reading</dt>
            <dd>{formatDateTime(meter.data.lastReadingAtUtc)}</dd>
          </div>
          <div>
            <dt>Supply</dt>
            <dd>
              <Badge tone={meter.data.desiredSupplyState === 'Open' ? 'success' : 'danger'}>
                {meter.data.desiredSupplyState}
              </Badge>
              {/* Desired vs reported, so a valve that failed to actuate stays
                  visible instead of being assumed closed. */}
              {meter.data.reportedSupplyState &&
              meter.data.reportedSupplyState !== meter.data.desiredSupplyState ? (
                <Badge tone="warn">Reported {meter.data.reportedSupplyState}</Badge>
              ) : null}
            </dd>
          </div>
        </dl>

        {meter.data.supplyStateReason ? (
          <p className="prose prose--muted">Supply note: {meter.data.supplyStateReason}</p>
        ) : null}
      </Card>

      <Card title="Consumption" description="Derived from the reading series, month by month">
        {consumption.isPending ? (
          <Spinner label="Loading consumption" />
        ) : consumption.isError ? (
          <ProblemAlert error={consumption.error} />
        ) : months.length === 0 ? (
          <EmptyState title="No consumption data yet" />
        ) : (
          <>
            <ul className="bars">
              {months.map((month) => {
                const value = month.consumptionM3 ?? 0
                const flags = month.flags ?? []
                return (
                  <li key={month.periodStartUtc} className="bars__row">
                    <span className="bars__label">
                      {new Date(month.periodStartUtc).toLocaleDateString(undefined, {
                        month: 'short',
                        year: 'numeric',
                      })}
                    </span>
                    <span className="bars__track">
                      <span
                        className="bars__fill"
                        style={{ inlineSize: peak > 0 ? `${(value / peak) * 100}%` : '0%' }}
                      />
                    </span>
                    <span className="bars__value">{formatVolume(value)}</span>
                    <span className="bars__flags">
                      {flags.map((flag) => (
                        <Badge key={flag} tone={flag === 'NoData' ? 'danger' : 'warn'}>
                          {humanizeFlag(flag)}
                        </Badge>
                      ))}
                    </span>
                  </li>
                )
              })}
            </ul>
            <p className="prose prose--muted">
              Flags explain caveats on a figure rather than hiding them — a month spanning a
              meter reset is summed across the discontinuity, and a month with no readings is
              reported as such instead of silently showing zero.
            </p>
          </>
        )}
      </Card>
    </div>
  )
}
