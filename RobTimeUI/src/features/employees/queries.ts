import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { components } from '@/api/schema'

export type Employee = components['schemas']['EmployeeResponse']
export type CreateEmployee = components['schemas']['CreateEmployeeRequest']
export type UpdateEmployee = components['schemas']['UpdateEmployeeRequest']

export interface EmployeeListParams {
  clientId: number
  search?: string
  page: number
  pageSize: number
}

// Same hierarchical shape as clientKeys — see features/clients/queries.ts for the reasoning. clientId
// is part of the key because the same page number means different rows under a different tenant.
export const employeeKeys = {
  all: ['employees'] as const,
  lists: () => [...employeeKeys.all, 'list'] as const,
  list: (params: EmployeeListParams) => [...employeeKeys.lists(), params] as const,
  details: () => [...employeeKeys.all, 'detail'] as const,
  detail: (id: number) => [...employeeKeys.details(), id] as const,
}

export function useEmployees(params: EmployeeListParams | null) {
  return useQuery({
    // `params` is null when no client is selected. `enabled` stops the request, but the key still
    // has to be a valid array, hence the placeholder.
    queryKey: params ? employeeKeys.list(params) : [...employeeKeys.lists(), 'none'],
    enabled: params !== null,
    queryFn: async () => {
      const { data, error } = await api.GET('/employees', {
        params: {
          query: {
            clientId: params!.clientId,
            search: params!.search || undefined,
            Page: params!.page,
            PageSize: params!.pageSize,
          },
        },
      })
      if (error) {
        throw error
      }
      return data
    },
    placeholderData: (previous) => previous,
  })
}

export function useEmployee(id: number) {
  return useQuery({
    queryKey: employeeKeys.detail(id),
    queryFn: async () => {
      const { data, error } = await api.GET('/employees/{id}', { params: { path: { id } } })
      if (error) {
        throw error
      }
      return data
    },
  })
}

export function useCreateEmployee() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: CreateEmployee) => {
      const { data, error } = await api.POST('/employees', { body })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: employeeKeys.lists() }),
  })
}

export function useUpdateEmployee(id: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: UpdateEmployee) => {
      const { data, error } = await api.PUT('/employees/{id}', { params: { path: { id } }, body })
      if (error) {
        throw error
      }
      return data
    },
    onMutate: async (body) => {
      await queryClient.cancelQueries({ queryKey: employeeKeys.detail(id) })
      const previous = queryClient.getQueryData<Employee>(employeeKeys.detail(id))
      if (previous) {
        queryClient.setQueryData<Employee>(employeeKeys.detail(id), { ...previous, ...body } as Employee)
      }
      return { previous }
    },
    onError: (_error, _body, context) => {
      if (context?.previous) {
        queryClient.setQueryData(employeeKeys.detail(id), context.previous)
      }
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: employeeKeys.detail(id) })
      void queryClient.invalidateQueries({ queryKey: employeeKeys.lists() })
    },
  })
}

export function useDeleteEmployee() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: number) => {
      const { error } = await api.DELETE('/employees/{id}', { params: { path: { id } } })
      if (error) {
        throw error
      }
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: employeeKeys.all }),
  })
}
