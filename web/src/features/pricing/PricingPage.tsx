import { PendingFeature } from '@/shared/ui/PendingFeature'

export function PricingPage() {
  return (
    <PendingFeature
      title="Pricing plans"
      summary="Admin editor for tariffs. Plans are immutable and versioned, so saving a change publishes a new version rather than editing the current one — the editor makes that explicit instead of pretending an edit is in place. Supports flat-rate and slab plans, with contiguous-band validation surfaced from the server."
      waitingOn={[
        'GET /api/v1/pricing-plans',
        'POST /api/v1/pricing-plans',
        'POST /api/v1/pricing-plans/{planId}/versions',
      ]}
    />
  )
}
