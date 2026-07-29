import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { components } from '@/api/schema'
import { timecardKeys } from '@/features/timecard/queries'

export type PunchChangeRequest = components['schemas']['PunchChangeRequestResponse']
export type DecidePunchChangeRequest = components['schemas']['DecidePunchChangeRequestRequest']

export const punchChangeRequestKeys = {
  all: ['punchChangeRequests'] as const,
  pending: () => [...punchChangeRequestKeys.all, 'pending'] as const,
}

// A supervisor's inbox: every Pending request across the tenant, not scoped to any one employee —
// there's no reporting-line concept yet (UI_PLAN.md §11's "restricted-visibility Supervisor tier"),
// so today Supervisor+ reviews everyone's requests, same as they can already act on any employee.
export function usePendingPunchChangeRequests() {
  return useQuery({
    queryKey: punchChangeRequestKeys.pending(),
    queryFn: async () => {
      const { data, error } = await api.GET('/punch-change-requests', {
        params: { query: { status: 'Pending', Page: 1, PageSize: 50 } },
      })
      if (error) {
        throw error
      }
      return data
    },
  })
}

export function useDecidePunchChangeRequest() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, body }: { id: number; body: DecidePunchChangeRequest }) => {
      const { data, error } = await api.POST('/punch-change-requests/{id}/decide', {
        params: { path: { id } },
        body,
      })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: punchChangeRequestKeys.all })
      // Broad rather than one employee's key: approving applies the change to a real punch, which
      // changes that employee's pay — and the reviewer has no reason to already know which employee
      // that was without re-reading the request they just decided.
      void queryClient.invalidateQueries({ queryKey: timecardKeys.all })
    },
  })
}
