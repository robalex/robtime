import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { components } from '@/api/schema'

export type PayrollExportProfile = components['schemas']['PayrollExportProfileResponse']
export type CreatePayrollExportProfile = components['schemas']['CreatePayrollExportProfileRequest']
export type UpdatePayrollExportProfile = components['schemas']['UpdatePayrollExportProfileRequest']

export interface PayrollExportProfileListParams {
  clientId: number
  page: number
  pageSize: number
}

export const payrollExportProfileKeys = {
  all: ['payrollExportProfiles'] as const,
  lists: () => [...payrollExportProfileKeys.all, 'list'] as const,
  list: (params: PayrollExportProfileListParams) => [...payrollExportProfileKeys.lists(), params] as const,
  details: () => [...payrollExportProfileKeys.all, 'detail'] as const,
  detail: (id: number) => [...payrollExportProfileKeys.details(), id] as const,
}

export function usePayrollExportProfiles(params: PayrollExportProfileListParams | null) {
  return useQuery({
    queryKey: params ? payrollExportProfileKeys.list(params) : [...payrollExportProfileKeys.lists(), 'none'],
    enabled: params !== null,
    queryFn: async () => {
      const { data, error } = await api.GET('/payroll-export-profiles', {
        params: { query: { clientId: params!.clientId, Page: params!.page, PageSize: params!.pageSize } },
      })
      if (error) {
        throw error
      }
      return data
    },
    placeholderData: (previous) => previous,
  })
}

export function usePayrollExportProfile(id: number) {
  return useQuery({
    queryKey: payrollExportProfileKeys.detail(id),
    queryFn: async () => {
      const { data, error } = await api.GET('/payroll-export-profiles/{id}', { params: { path: { id } } })
      if (error) {
        throw error
      }
      return data
    },
  })
}

export function useCreatePayrollExportProfile() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: CreatePayrollExportProfile) => {
      const { data, error } = await api.POST('/payroll-export-profiles', { body })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: payrollExportProfileKeys.lists() }),
  })
}

export function useUpdatePayrollExportProfile(id: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: UpdatePayrollExportProfile) => {
      const { data, error } = await api.PUT('/payroll-export-profiles/{id}', { params: { path: { id } }, body })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: payrollExportProfileKeys.detail(id) })
      void queryClient.invalidateQueries({ queryKey: payrollExportProfileKeys.lists() })
    },
  })
}

export function useDeletePayrollExportProfile() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: number) => {
      const { error } = await api.DELETE('/payroll-export-profiles/{id}', { params: { path: { id } } })
      if (error) {
        throw error
      }
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: payrollExportProfileKeys.all }),
  })
}
