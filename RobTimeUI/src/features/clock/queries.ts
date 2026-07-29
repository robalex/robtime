import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { components } from '@/api/schema'

export type ClockStatus = components['schemas']['ClockStatusResponse']
export type CreatePunch = components['schemas']['CreatePunchRequest']

// No employeeId in the key — /me/clock-status is inherently the signed-in caller's own status, and
// a different caller means a different session entirely (the whole query cache is torn down on
// logout). Adding one would imply this cache could hold two employees at once, which it can't.
export const clockKeys = {
  all: ['clock'] as const,
  status: () => [...clockKeys.all, 'status'] as const,
}

export function useClockStatus(enabled: boolean) {
  return useQuery({
    queryKey: clockKeys.status(),
    // Disabled for an account with no linked Employee record — that endpoint 404s by design, and a
    // 404 rendered as an error state would be wrong: "you aren't an employee" isn't a failure.
    enabled,
    queryFn: async () => {
      const { data, error } = await api.GET('/me/clock-status')
      if (error) {
        throw error
      }
      return data
    },
  })
}

export function useClockPunch() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: CreatePunch) => {
      const { data, error } = await api.POST('/punches', { body })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: clockKeys.status() })
    },
  })
}
