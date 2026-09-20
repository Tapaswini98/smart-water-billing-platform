import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { api, unwrap } from '@/shared/api/client'

export const invoiceKeys = {
  all: ['invoices'] as const,
  list: (page: number, pageSize: number) => [...invoiceKeys.all, 'list', page, pageSize] as const,
  detail: (invoiceId: string) => [...invoiceKeys.all, invoiceId] as const,
  runs: ['billing-runs'] as const,
}

export function useInvoices(page: number, pageSize: number) {
  return useQuery({
    queryKey: invoiceKeys.list(page, pageSize),
    queryFn: () => unwrap(api.GET('/api/v1/invoices', { params: { query: { page, pageSize } } })),
  })
}

export function useInvoice(invoiceId: string) {
  return useQuery({
    queryKey: invoiceKeys.detail(invoiceId),
    queryFn: () =>
      unwrap(api.GET('/api/v1/invoices/{invoiceId}', { params: { path: { invoiceId } } })),
    enabled: invoiceId.length > 0,
  })
}

export function useBillingRuns() {
  return useQuery({
    queryKey: invoiceKeys.runs,
    queryFn: () => unwrap(api.GET('/api/v1/billing-runs', { params: { query: { limit: 10 } } })),
  })
}

export function useGenerateInvoices() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (input: { dryRun: boolean; issueImmediately: boolean }) =>
      unwrap(
        api.POST('/api/v1/billing-runs', {
          body: { dryRun: input.dryRun, issueImmediately: input.issueImmediately },
        }),
      ),
    onSuccess: (_data, variables) => {
      // A dry run changes nothing, so there is nothing to invalidate. Clearing the
      // cache anyway would make a preview look like it had side effects.
      if (!variables.dryRun) {
        void queryClient.invalidateQueries({ queryKey: invoiceKeys.all })
        void queryClient.invalidateQueries({ queryKey: invoiceKeys.runs })
      }
    },
  })
}

export function usePayInvoice(invoiceId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (input: { amount: number; idempotencyKey: string }) =>
      unwrap(
        api.POST('/api/v1/invoices/{invoiceId}/payments', {
          params: { path: { invoiceId } },
          body: {
            amount: input.amount,
            provider: 'mock',
            // Generated once per attempt and reused across retries, so a double
            // submit is recognised as the same payment rather than charged twice.
            idempotencyKey: input.idempotencyKey,
          },
        }),
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: invoiceKeys.all })
    },
  })
}
