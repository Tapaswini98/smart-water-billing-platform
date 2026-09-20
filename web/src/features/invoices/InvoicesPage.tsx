import { useState } from 'react'
import { Link } from 'react-router-dom'

import { useAuth } from '@/features/auth/useAuth'
import { formatDate, formatMoney, formatVolume } from '@/shared/lib/format'
import { Badge } from '@/shared/ui/Badge'
import { Button } from '@/shared/ui/Button'
import { Card } from '@/shared/ui/Card'
import { EmptyState } from '@/shared/ui/EmptyState'
import { ProblemAlert } from '@/shared/ui/ProblemAlert'
import { Spinner } from '@/shared/ui/Spinner'

import { useGenerateInvoices, useInvoices } from './queries'

const PAGE_SIZE = 25

export function InvoicesPage() {
  const { isAdmin } = useAuth()
  const [page, setPage] = useState(1)
  const { data, isPending, isError, error } = useInvoices(page, PAGE_SIZE)
  const generate = useGenerateInvoices()

  if (isPending) {
    return (
      <Card title="Invoices">
        <Spinner label="Loading invoices" />
      </Card>
    )
  }

  if (isError) {
    return (
      <Card title="Invoices">
        <ProblemAlert error={error} />
      </Card>
    )
  }

  const invoices = data.invoices ?? []
  const totalCount = data.totalCount ?? 0
  const lastPage = Math.max(1, Math.ceil(totalCount / PAGE_SIZE))
  const run = generate.data

  return (
    <div className="stack">
      {isAdmin ? (
        <Card
          title="Generate previous month's invoices"
          description="Idempotent — re-running a period cannot double-bill. Preview first if you want to see the outcome without issuing anything."
        >
          <div className="inline-form">
            <Button
              variant="secondary"
              loading={generate.isPending && generate.variables?.dryRun === true}
              onClick={() => generate.mutate({ dryRun: true, issueImmediately: false })}
            >
              Preview (dry run)
            </Button>
            <Button
              loading={generate.isPending && generate.variables?.dryRun === false}
              onClick={() => generate.mutate({ dryRun: false, issueImmediately: true })}
            >
              Generate and issue
            </Button>
          </div>

          <ProblemAlert error={generate.error} />

          {run ? (
            <>
              <p className="prose">
                <strong>
                  {run.isDryRun ? 'Preview' : 'Run'} for {formatDate(run.periodStartUtc)} –{' '}
                  {formatDate(run.periodEndUtc)}
                </strong>{' '}
                — {run.invoicesGenerated} generated, {run.skipped} skipped, {run.failed} failed,{' '}
                {formatMoney(run.totalBilledAmount)} billed.
                {run.isDryRun ? ' Nothing was saved.' : null}
              </p>

              <div className="table-scroll">
                <table className="table">
                  <thead>
                    <tr>
                      <th scope="col">Meter</th>
                      <th scope="col">Outcome</th>
                      <th scope="col" className="table__num">
                        Consumption
                      </th>
                      <th scope="col" className="table__num">
                        Amount
                      </th>
                      <th scope="col">Why</th>
                    </tr>
                  </thead>
                  <tbody>
                    {(run.items ?? []).map((item) => (
                      <tr key={item.meterId}>
                        <td>{item.meterSerial}</td>
                        <td>
                          <Badge tone={outcomeTone(item.outcome)}>{item.outcome}</Badge>
                        </td>
                        <td className="table__num">{formatVolume(item.consumptionM3)}</td>
                        <td className="table__num">{formatMoney(item.totalAmount)}</td>
                        {/* Every meter carries a reason. A run that silently skips
                            meters is how revenue goes missing unnoticed. */}
                        <td className="table__muted">{item.message ?? '—'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </>
          ) : null}
        </Card>
      ) : null}

      <Card
        title="Invoices"
        description={isAdmin ? `${totalCount} issued` : 'Your current and previous invoices'}
      >
        {invoices.length === 0 ? (
          <EmptyState
            title="No invoices yet"
            description={
              isAdmin
                ? 'Run invoice generation for the previous month to create them.'
                : 'Invoices appear here once your supplier has issued them.'
            }
          />
        ) : (
          <>
            <div className="table-scroll">
              <table className="table">
                <thead>
                  <tr>
                    <th scope="col">Invoice</th>
                    <th scope="col">Period</th>
                    <th scope="col">Meter</th>
                    {isAdmin ? <th scope="col">Customer</th> : null}
                    <th scope="col" className="table__num">
                      Consumption
                    </th>
                    <th scope="col" className="table__num">
                      Total
                    </th>
                    <th scope="col">Status</th>
                  </tr>
                </thead>
                <tbody>
                  {invoices.map((invoice) => (
                    <tr key={invoice.id}>
                      <td>
                        <Link to={`/invoices/${invoice.id}`}>{invoice.invoiceNumber}</Link>
                      </td>
                      <td className="table__muted">{formatDate(invoice.periodStartUtc)}</td>
                      <td>{invoice.meterSerial}</td>
                      {isAdmin ? <td>{invoice.customerName ?? '—'}</td> : null}
                      <td className="table__num">{formatVolume(invoice.consumptionM3)}</td>
                      <td className="table__num">
                        {formatMoney(invoice.totalAmount, invoice.currency)}
                      </td>
                      <td>
                        <Badge tone={statusTone(invoice.status)}>{invoice.status}</Badge>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {lastPage > 1 ? (
              <nav className="pager" aria-label="Pagination">
                <Button
                  variant="secondary"
                  disabled={page <= 1}
                  onClick={() => setPage((p) => p - 1)}
                >
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
    </div>
  )
}

function statusTone(status: string | undefined) {
  switch (status) {
    case 'Paid':
      return 'success' as const
    case 'Issued':
      return 'info' as const
    case 'Overdue':
      return 'danger' as const
    case 'Void':
      return 'neutral' as const
    default:
      return 'neutral' as const
  }
}

function outcomeTone(outcome: string | undefined) {
  switch (outcome) {
    case 'Generated':
      return 'success' as const
    case 'GeneratedWithoutData':
      return 'warn' as const
    case 'AlreadyBilled':
      return 'info' as const
    case 'Failed':
      return 'danger' as const
    default:
      return 'neutral' as const
  }
}
