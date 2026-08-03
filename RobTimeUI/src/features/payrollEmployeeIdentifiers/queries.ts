import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { components } from '@/api/schema'

export type PayrollEmployeeIdentifier = components['schemas']['PayrollEmployeeIdentifierResponse']
export type CreatePayrollEmployeeIdentifier = components['schemas']['CreatePayrollEmployeeIdentifierRequest']
export type UpdatePayrollEmployeeIdentifier = components['schemas']['UpdatePayrollEmployeeIdentifierRequest']

// An identifier has no meaning apart from the profile it's for — same parent-scoped shape as
// payrollEarningCodeMappings/queries.ts.
export const payrollEmployeeIdentifierKeys = {
  all: ['payrollEmployeeIdentifiers'] as const,
  list: (profileId: number) => [...payrollEmployeeIdentifierKeys.all, 'list', profileId] as const,
}

export function usePayrollEmployeeIdentifiers(profileId: number) {
  return useQuery({
    queryKey: payrollEmployeeIdentifierKeys.list(profileId),
    queryFn: async () => {
      const { data, error } = await api.GET('/payroll-export-profiles/{profileId}/employee-identifiers', {
        params: { path: { profileId } },
      })
      if (error) {
        throw error
      }
      return data
    },
  })
}

export function useCreatePayrollEmployeeIdentifier(profileId: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: CreatePayrollEmployeeIdentifier) => {
      const { data, error } = await api.POST('/payroll-export-profiles/{profileId}/employee-identifiers', {
        params: { path: { profileId } },
        body,
      })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: payrollEmployeeIdentifierKeys.list(profileId) }),
  })
}

export function useUpdatePayrollEmployeeIdentifier(profileId: number, id: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: UpdatePayrollEmployeeIdentifier) => {
      const { data, error } = await api.PUT('/payroll-export-profiles/{profileId}/employee-identifiers/{id}', {
        params: { path: { profileId, id } },
        body,
      })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: payrollEmployeeIdentifierKeys.list(profileId) }),
  })
}

export function useDeletePayrollEmployeeIdentifier(profileId: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: number) => {
      const { error } = await api.DELETE('/payroll-export-profiles/{profileId}/employee-identifiers/{id}', {
        params: { path: { profileId, id } },
      })
      if (error) {
        throw error
      }
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: payrollEmployeeIdentifierKeys.list(profileId) }),
  })
}
