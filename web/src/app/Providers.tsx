import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useState, type ReactNode } from 'react'

import { AuthProvider } from '@/features/auth/AuthProvider'
import { ApiProblem } from '@/shared/api/problem'

function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        // Retrying a 401, 403 or 404 wastes a round trip and delays the error the
        // user needs to see. Retry only what might genuinely be transient.
        retry: (failureCount, error) => {
          if (error instanceof ApiProblem && error.status >= 400 && error.status < 500) {
            return false
          }
          return failureCount < 2
        },
        refetchOnWindowFocus: false,
        staleTime: 30_000,
      },
      mutations: { retry: false },
    },
  })
}

export function Providers({ children }: { children: ReactNode }) {
  // Created in state, not at module scope, so a test can mount a fresh app with an
  // empty cache instead of inheriting one from a previous test.
  const [queryClient] = useState(createQueryClient)

  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>{children}</AuthProvider>
    </QueryClientProvider>
  )
}
