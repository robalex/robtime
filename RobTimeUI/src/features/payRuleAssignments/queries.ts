import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { components } from '@/api/schema'

export type PayRuleAssignment = components['schemas']['PayRuleAssignmentResponse']
export type CreatePayRuleAssignment = components['schemas']['CreatePayRuleAssignmentRequest']
export type UpdatePayRuleAssignment = components['schemas']['UpdatePayRuleAssignmentRequest']

// Keyed by employeeId only — same reasoning as positionAssignmentKeys: an assignment has no meaning
// apart from its employee.
export const payRuleAssignmentKeys = {
  all: ['payRuleAssignments'] as const,
  lists: () => [...payRuleAssignmentKeys.all, 'list'] as const,
  list: (employeeId: number) => [...payRuleAssignmentKeys.lists(), employeeId] as const,
}

export function usePayRuleAssignments(employeeId: number) {
  return useQuery({
    queryKey: payRuleAssignmentKeys.list(employeeId),
    queryFn: async () => {
      const { data, error } = await api.GET('/employees/{employeeId}/payrules', {
        params: { path: { employeeId } },
      })
      if (error) {
        throw error
      }
      return data
    },
  })
}

export function useCreatePayRuleAssignment(employeeId: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: CreatePayRuleAssignment) => {
      const { data, error } = await api.POST('/employees/{employeeId}/payrules', {
        params: { path: { employeeId } },
        body,
      })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: payRuleAssignmentKeys.list(employeeId) }),
  })
}

export function useUpdatePayRuleAssignment(employeeId: number, id: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: UpdatePayRuleAssignment) => {
      const { data, error } = await api.PUT('/employees/{employeeId}/payrules/{id}', {
        params: { path: { employeeId, id } },
        body,
      })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: payRuleAssignmentKeys.list(employeeId) }),
  })
}

export function useDeletePayRuleAssignment(employeeId: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: number) => {
      const { error } = await api.DELETE('/employees/{employeeId}/payrules/{id}', {
        params: { path: { employeeId, id } },
      })
      if (error) {
        throw error
      }
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: payRuleAssignmentKeys.list(employeeId) }),
  })
}
