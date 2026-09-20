import { useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'

import { formatDate, formatDateTime, formatMoney, formatVolume, humanizeFlag } from '@/shared/lib/format'
import { Badge } from '@/shared/ui/Badge'
import { Button } from '@/shared/ui/Button'
import { Card } from '@/shared/ui/Card'
import { ProblemAlert } from '@/shared/ui/ProblemAlert'
import { Spinner } from '@/shared/ui/Spinner'

import { useInvoice, usePayInvoice } from './queries'

export function InvoiceDetailPage() {
  const { invoiceId = '' } = useParams<{ invoiceId: string }>()
  const { data, isPending, isError, error } = useInvoice(invoiceId)
  const pay = usePayInvoice(invoiceId)

  // Stable for the life of this screen, so a double-click or a retry after a
  // timeout is recognised by the server as the same payment.
  const [idempotencyKey] = useState(() => `checkout-${crypto.randomUUID()}`)

  const lines = useMemo(() => data?.lines ?? [], [data])

  if (isPending) {
    return (
      <Card title="Invoice">
        <Spinner label="Loading invoice" />
      </Card>
    )
  }

  if (isError) {
    return (
      <Card title="Invoice">
        <ProblemAlert error={error} />
        <p className="prose prose--muted">
          <Link to="/invoices">Back to invoices</Link>
        </p>
      </Card>
    )
  }

  const flags = data.consumptionFlags ?? []
  const amountDue = data.amountDue ?? 0
  const currency = data.currency ?? 'INR'

  return (
    <div className="stack">
      <Card
        title={data.invoiceNumber}
        description={`${formatDate(data.periodStartUtc)} – ${formatDate(data.periodEndUtc)}`}
        actions={<Badge tone={data.status === 'Paid' ? 'success' : 'info'}>{data.status}</Badge>}
      >
        <dl className="detail-grid">
          <div>
            <dt>Meter</dt>
            <dd>
              <Link to={`/meters/${data.meterId}`}>{data.meterSerial}</Link>
            </dd>
          </div>
          <div>
            <dt>Customer</dt>
            <dd>{data.customerName ?? '—'}</dd>
          </div>
          <div>
            <dt>Issued</dt>
            <dd>{formatDateTime(data.issuedAtUtc)}</dd>
          </div>
          <div>
            <dt>Due</dt>
            <dd>{formatDate(data.dueAtUtc)}</dd>
          </div>
        </dl>
      </Card>

      <Card
        title="How this figure was reached"
        description="Meter readings, not an estimate"
      >
        <dl className="detail-grid">
          <div>
            <dt>Opening reading</dt>
            <dd>
              {formatVolume(data.openingTotalM3)}
              <span className="table__sub">{formatDateTime(data.openingReadingAtUtc)}</span>
            </dd>
          </div>
          <div>
            <dt>Closing reading</dt>
            <dd>
              {formatVolume(data.closingTotalM3)}
              <span className="table__sub">{formatDateTime(data.closingReadingAtUtc)}</span>
            </dd>
          </div>
          <div>
            <dt>Consumption</dt>
            <dd>
              <strong>{formatVolume(data.consumptionM3)}</strong>
              <span className="table__sub">from {data.readingCount} readings</span>
            </dd>
          </div>
        </dl>

        {flags.length > 0 ? (
          <>
            <div className="badge-row">
              {flags.map((flag) => (
                <Badge key={flag} tone={flag === 'NoData' ? 'danger' : 'warn'}>
                  {humanizeFlag(flag)}
                </Badge>
              ))}
            </div>
            <p className="prose prose--muted">
              {/* Stated on the invoice rather than buried in a log: a customer
                  querying a bill deserves to see why the number is what it is. */}
              {flags.includes('MeterResetDuringPeriod')
                ? 'The meter register was replaced or reset during this period. Consumption was summed across the discontinuity rather than treated as negative. '
                : null}
              {flags.includes('NoData')
                ? 'The meter reported no readings in this period, so only the standing charge applies. '
                : null}
              {flags.includes('PartialPeriod')
                ? 'No reading existed at the start of the period, so consumption is measured from the first reading inside it. '
                : null}
              {flags.includes('StaleClosingReading')
                ? 'The last reading is well before the period end — the meter may have stopped reporting. '
                : null}
            </p>
          </>
        ) : null}
      </Card>

      <Card
        title="Charges"
        description={`${data.pricingPlanName} — version ${data.pricingPlanVersionNumber}, as it stood when this invoice was issued`}
      >
        <div className="table-scroll">
          <table className="table">
            <thead>
              <tr>
                <th scope="col">Description</th>
                <th scope="col" className="table__num">
                  Units
                </th>
                <th scope="col" className="table__num">
                  Rate
                </th>
                <th scope="col" className="table__num">
                  Amount
                </th>
              </tr>
            </thead>
            <tbody>
              {lines.map((line) => (
                <tr key={line.sortOrder}>
                  <td>
                    {line.description}
                    {line.kind === 'Consumption' && line.bandFromM3 !== null ? (
                      <span className="table__sub">
                        band {line.bandFromM3}–{line.bandToM3 ?? '∞'} m³
                      </span>
                    ) : null}
                  </td>
                  <td className="table__num">
                    {line.kind === 'Consumption' ? formatVolume(line.unitsM3) : '—'}
                  </td>
                  <td className="table__num">
                    {line.kind === 'Consumption' ? formatMoney(line.ratePerM3, currency) : '—'}
                  </td>
                  <td className="table__num">{formatMoney(line.amount, currency)}</td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr>
                <th scope="row" colSpan={3}>
                  Subtotal
                </th>
                <td className="table__num">
                  {formatMoney((data.fixedCharge ?? 0) + (data.usageCharge ?? 0), currency)}
                </td>
              </tr>
              <tr>
                <th scope="row" colSpan={3}>
                  Total
                </th>
                <td className="table__num">
                  <strong>{formatMoney(data.totalAmount, currency)}</strong>
                </td>
              </tr>
              {(data.amountPaid ?? 0) > 0 ? (
                <tr>
                  <th scope="row" colSpan={3}>
                    Paid
                  </th>
                  <td className="table__num">−{formatMoney(data.amountPaid, currency)}</td>
                </tr>
              ) : null}
              <tr>
                <th scope="row" colSpan={3}>
                  Amount due
                </th>
                <td className="table__num">
                  <strong>{formatMoney(amountDue, currency)}</strong>
                </td>
              </tr>
            </tfoot>
          </table>
        </div>

        <p className="prose prose--muted">
          These lines were written when the invoice was generated and are never recomputed, so
          this bill reads today exactly as it did when it was issued — even if the tariff has
          since changed.
        </p>
      </Card>

      {amountDue > 0 && data.status !== 'Void' ? (
        <Card title="Payment">
          <ProblemAlert error={pay.error} />
          {pay.isSuccess ? (
            <p className="prose">
              Payment of {formatMoney(pay.data.amount, currency)} recorded. Invoice is now{' '}
              {pay.data.invoiceStatus}.
            </p>
          ) : (
            <>
              <p className="prose">
                Pay {formatMoney(amountDue, currency)} via the mock provider.
              </p>
              <Button
                loading={pay.isPending}
                onClick={() => pay.mutate({ amount: amountDue, idempotencyKey })}
              >
                Pay {formatMoney(amountDue, currency)}
              </Button>
              <p className="prose prose--muted">
                No real payment gateway is wired up, and no gateway credentials exist in this
                repository. The parts worth demonstrating — recording every attempt, and
                idempotency so a retried checkout cannot charge twice — are real.
              </p>
            </>
          )}
        </Card>
      ) : null}
    </div>
  )
}
