import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { components } from '@/api/schema'

export type Timecard = components['schemas']['TimecardResponse']
export type DraftPunchEntry = components['schemas']['DraftPunchEntry']
export type BulkPunchPreview = components['schemas']['BulkPunchPreviewResponse']
export type CreatePunch = components['schemas']['CreatePunchRequest']

export const timecardKeys = {
  all: ['timecard'] as const,
  // date is part of the key (not just employeeId) so navigating to a different pay period is a
  // distinct cache entry rather than a refetch that clobbers the period the user was just looking
  // at — same reasoning as employeeKeys.list keying on the full params object.
  detail: (employeeId: number, date?: string) =>
    [...timecardKeys.all, employeeId, date ?? 'current'] as const,
  // Partial key for invalidation only — a batch save doesn't know which cached period(s) its rows
  // landed in, so every cached period for this employee is invalidated rather than just `date`.
  forEmployee: (employeeId: number) => [...timecardKeys.all, employeeId] as const,
}

export function useTimecard(employeeId: number, date?: string) {
  return useQuery({
    queryKey: timecardKeys.detail(employeeId, date),
    queryFn: async () => {
      const { data, error } = await api.GET('/employees/{id}/timecard', {
        params: { path: { id: employeeId }, query: date ? { date } : undefined },
      })
      if (error) {
        throw error
      }
      return data
    },
  })
}

export function useApproveTimecard(employeeId: number, date?: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async () => {
      const { data, error } = await api.POST('/employees/{id}/timecard/approve', {
        params: { path: { id: employeeId }, query: date ? { date } : undefined },
      })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: (data) => queryClient.setQueryData(timecardKeys.detail(employeeId, date), data),
  })
}

// A mutation, not a query: the "input" is whatever the grid currently holds, which changes on every
// keystroke — there's no stable queryKey to cache against, and the caller (BulkPunchEntry) already
// debounces its own calls to mutateAsync.
export function usePreviewTimecard(employeeId: number, date?: string) {
  return useMutation({
    mutationFn: async (draftPunches: DraftPunchEntry[]) => {
      const { data, error } = await api.POST('/employees/{id}/timecard/preview', {
        params: { path: { id: employeeId }, query: date ? { date } : undefined },
        body: { draftPunches },
      })
      if (error) {
        throw error
      }
      return data
    },
  })
}

export function useCreatePunchBatch(employeeId: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (punches: CreatePunch[]) => {
      const { data, error } = await api.POST('/punches/batch', { body: { punches } })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: timecardKeys.forEmployee(employeeId) }),
  })
}

export function useUnapproveTimecard(employeeId: number, date?: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async () => {
      const { data, error } = await api.POST('/employees/{id}/timecard/unapprove', {
        params: { path: { id: employeeId }, query: date ? { date } : undefined },
      })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: (data) => queryClient.setQueryData(timecardKeys.detail(employeeId, date), data),
  })
}
