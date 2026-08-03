import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { components } from '@/api/schema'

export type PayrollEarningCodeMapping = components['schemas']['PayrollEarningCodeMappingResponse']
export type CreatePayrollEarningCodeMapping = components['schemas']['CreatePayrollEarningCodeMappingRequest']
export type UpdatePayrollEarningCodeMapping = components['schemas']['UpdatePayrollEarningCodeMappingRequest']

// A mapping has no meaning apart from the profile it's for (the backend contract's own doc comment)
// — every hook here takes profileId as its first argument, same shape as payRuleAssignments/queries.ts.
export const payrollEarningCodeMappingKeys = {
  all: ['payrollEarningCodeMappings'] as const,
  list: (profileId: number) => [...payrollEarningCodeMappingKeys.all, 'list', profileId] as const,
}

export function usePayrollEarningCodeMappings(profileId: number) {
  return useQuery({
    queryKey: payrollEarningCodeMappingKeys.list(profileId),
    queryFn: async () => {
      const { data, error } = await api.GET('/payroll-export-profiles/{profileId}/earning-codes', {
        params: { path: { profileId } },
      })
      if (error) {
        throw error
      }
      return data
    },
  })
}

export function useCreatePayrollEarningCodeMapping(profileId: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: CreatePayrollEarningCodeMapping) => {
      const { data, error } = await api.POST('/payroll-export-profiles/{profileId}/earning-codes', {
        params: { path: { profileId } },
        body,
      })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: payrollEarningCodeMappingKeys.list(profileId) }),
  })
}

export function useUpdatePayrollEarningCodeMapping(profileId: number, id: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: UpdatePayrollEarningCodeMapping) => {
      const { data, error } = await api.PUT('/payroll-export-profiles/{profileId}/earning-codes/{id}', {
        params: { path: { profileId, id } },
        body,
      })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: payrollEarningCodeMappingKeys.list(profileId) }),
  })
}

export function useDeletePayrollEarningCodeMapping(profileId: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: number) => {
      const { error } = await api.DELETE('/payroll-export-profiles/{profileId}/earning-codes/{id}', {
        params: { path: { profileId, id } },
      })
      if (error) {
        throw error
      }
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: payrollEarningCodeMappingKeys.list(profileId) }),
  })
}
