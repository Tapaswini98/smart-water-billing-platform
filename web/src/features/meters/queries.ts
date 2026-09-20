import { useQuery } from '@tanstack/react-query'

import { api, unwrap } from '@/shared/api/client'

/**
 * Query keys are namespaced per feature so an invalidation after, say, assigning a
 * meter can target exactly the affected data rather than clearing the cache.
 */
export const meterKeys = {
  all: ['meters'] as const,
  list: (params: MeterListParams) => [...meterKeys.all, 'list', params] as const,
  detail: (meterId: string) => [...meterKeys.all, meterId] as const,
  readings: (meterId: string, params: ReadingsParams) =>
    [...meterKeys.all, meterId, 'readings', params] as const,
  consumption: (meterId: string, months: number) =>
    [...meterKeys.all, meterId, 'consumption', months] as const,
}

export interface MeterListParams {
  page: number
  pageSize: number
  search?: string | undefined
}

export interface ReadingsParams {
  page: number
  pageSize: number
  from?: string | undefined
  to?: string | undefined
}

export function useMeters(params: MeterListParams) {
  return useQuery({
    queryKey: meterKeys.list(params),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/meters', {
          params: {
            query: {
              page: params.page,
              pageSize: params.pageSize,
              ...(params.search ? { search: params.search } : {}),
            },
          },
        }),
      ),
  })
}

export function useMeter(meterId: string) {
  return useQuery({
    queryKey: meterKeys.detail(meterId),
    queryFn: () =>
      unwrap(api.GET('/api/v1/meters/{meterId}', { params: { path: { meterId } } })),
    enabled: meterId.length > 0,
  })
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
    // that spends requests to display the same rows.
    staleTime: 60_000,
  })
}

export function useMonthlyConsumption(meterId: string, months = 6) {
  return useQuery({
    queryKey: meterKeys.consumption(meterId, months),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/meters/{meterId}/consumption/monthly', {
          params: { path: { meterId }, query: { months } },
        }),
      ),
    enabled: meterId.length > 0,
    staleTime: 60_000,
  })
}
