import { PendingFeature } from '@/shared/ui/PendingFeature'

export function InvoicesPage() {
  return (
    <PendingFeature
      title="Invoices"
      summary="Invoice history for the signed-in customer, and every invoice for an admin. The detail view renders the slab breakdown stored on the invoice — band, units, rate and amount per line — so a bill can be checked against the tariff that produced it without recomputing anything."
      waitingOn={[
        'GET /api/v1/invoices',
        'GET /api/v1/invoices/{invoiceId}',
        'POST /api/v1/billing-runs',
      ]}
    />
  )
}
