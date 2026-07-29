import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { components } from '@/api/schema'
import { timecardKeys } from '@/features/timecard/queries'

export type PunchChangeRequest = components['schemas']['PunchChangeRequestResponse']
export type DecidePunchChangeRequest = components['schemas']['DecidePunchChangeRequestRequest']
export type SubmitPunchChangeRequest = components['schemas']['SubmitPunchChangeRequestRequest']

export const punchChangeRequestKeys = {
  all: ['punchChangeRequests'] as const,
  pending: () => [...punchChangeRequestKeys.all, 'pending'] as const,
  mine: (employeeId: number) => [...punchChangeRequestKeys.all, 'mine', employeeId] as const,
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

// An employee's own submitted requests, any status — the visibility half of self-service punch
// requests: submitting into a queue with no way to check what happened to it isn't a finished
// feature. The API pins an Employee caller to their own requests regardless of the employeeId
// passed (PunchChangeRequestService.ListAsync), so this is really just for cache-keying and for
// Supervisor+ reuse later, not the source of truth for who can see what.
export function useMyPunchChangeRequests(employeeId: number, enabled: boolean) {
  return useQuery({
    queryKey: punchChangeRequestKeys.mine(employeeId),
    enabled,
    queryFn: async () => {
      const { data, error } = await api.GET('/punch-change-requests', {
        params: { query: { employeeId, Page: 1, PageSize: 50 } },
      })
      if (error) {
        throw error
      }
      return data
    },
  })
}

export function useSubmitPunchChangeRequest(employeeId: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: SubmitPunchChangeRequest) => {
      const { data, error } = await api.POST('/punch-change-requests', { body })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: punchChangeRequestKeys.mine(employeeId) })
      // In case this account is also a Supervisor reviewing their own submissions (an edge case,
      // but a cheap one to cover) — same broad invalidation useDecidePunchChangeRequest uses.
      void queryClient.invalidateQueries({ queryKey: punchChangeRequestKeys.pending() })
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
