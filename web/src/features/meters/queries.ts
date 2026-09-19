import { useQuery } from '@tanstack/react-query'

import { api, unwrap } from '@/shared/api/client'

/**
 * Query keys are namespaced per feature so an invalidation after, say, ingesting a
 * reading can target exactly the affected meter rather than clearing the cache.
 */
export const meterKeys = {
  all: ['meters'] as const,
  readings: (meterId: string, params: ReadingsParams) =>
    [...meterKeys.all, meterId, 'readings', params] as const,
}

export interface ReadingsParams {
  page: number
  pageSize: number
  from?: string | undefined
  to?: string | undefined
}

export function useMeterReadings(meterId: string, params: ReadingsParams) {
  return useQuery({
    queryKey: meterKeys.readings(meterId, params),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/meters/{meterId}/readings', {
          params: {
            path: { meterId },
            query: {
              page: params.page,
              pageSize: params.pageSize,
              ...(params.from !== undefined ? { from: params.from } : {}),
              ...(params.to !== undefined ? { to: params.to } : {}),
            },
          },
        }),
      ),
    // Readings are append-only and arrive every 15 minutes; re-fetching faster than
    // that just spends requests to display the same rows.
    staleTime: 60_000,
  })
}
