import { useQuery } from '@tanstack/react-query'

import { api, unwrap } from '@/shared/api/client'
import { formatMoney } from '@/shared/lib/format'
import { Badge } from '@/shared/ui/Badge'
import { Card } from '@/shared/ui/Card'
import { EmptyState } from '@/shared/ui/EmptyState'
import { ProblemAlert } from '@/shared/ui/ProblemAlert'
import { Spinner } from '@/shared/ui/Spinner'

export function PricingPage() {
  const { data, isPending, isError, error } = useQuery({
    queryKey: ['pricing-plans'],
    queryFn: () => unwrap(api.GET('/api/v1/pricing-plans')),
  })

  if (isPending) {
    return (
      <Card title="Pricing plans">
        <Spinner label="Loading plans" />
      </Card>
    )
  }

  if (isError) {
    return (
      <Card title="Pricing plans">
        <ProblemAlert error={error} />
      </Card>
    )
  }

  if (data.length === 0) {
    return (
      <Card title="Pricing plans">
        <EmptyState title="No pricing plans" description="Create a plan before billing anything." />
      </Card>
    )
  }

  return (
    <div className="stack">
      {data.map((plan) => (
        <Card
          key={plan.id}
          title={plan.name}
          description={plan.description ?? undefined}
          actions={plan.isDefault ? <Badge tone="info">Default</Badge> : null}
        >
          <p className="prose prose--muted">
            {plan.meterCount} meter{plan.meterCount === 1 ? '' : 's'} on this plan · {plan.currency}
          </p>

          {(plan.versions ?? []).map((version) => (
            <div key={version.id} className="version">
              <h3 className="version__title">
                Version {version.versionNumber}
                {version.isCurrent ? <Badge tone="success">In force</Badge> : null}
                {version.effectiveToUtc ? <Badge tone="neutral">Superseded</Badge> : null}
              </h3>

              <p className="prose prose--muted">
                {version.mode === 'FlatRate'
                  ? `Flat rate ${formatMoney(version.ratePerM3, plan.currency)}/m³`
                  : `Slab (${version.slabMode === 'Progressive' ? 'progressive / telescopic' : 'whole volume at reached band'})`}
                {' · standing charge '}
                {formatMoney(version.fixedCharge, plan.currency)}
                {version.taxRatePercent > 0 ? ` · tax ${version.taxRatePercent}%` : null}
              </p>

              {version.mode === 'Slab' && (version.slabs ?? []).length > 0 ? (
                <div className="table-scroll">
                  <table className="table">
                    <thead>
                      <tr>
                        <th scope="col">Band</th>
                        <th scope="col" className="table__num">
                          Rate per m³
                        </th>
                      </tr>
                    </thead>
                    <tbody>
                      {(version.slabs ?? []).map((slab) => (
                        <tr key={slab.sortOrder}>
                          <td>
                            {slab.toM3 === null
                              ? `Above ${slab.fromM3} m³`
                              : `${slab.fromM3}–${slab.toM3} m³`}
                          </td>
                          <td className="table__num">
                            {formatMoney(slab.ratePerM3, plan.currency)}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              ) : null}
            </div>
          ))}

          <p className="prose prose--muted">
            {/* The version history is the point: an invoice cites the version it was
                priced under, so changing a tariff can never restate an issued bill. */}
            Versions are immutable. Publishing new rates closes the current version and opens a
            successor, so an invoice issued under version 1 still prices as version 1 forever.
          </p>
        </Card>
      ))}
    </div>
  )
}
