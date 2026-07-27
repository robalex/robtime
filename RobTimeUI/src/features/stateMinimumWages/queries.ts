import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { components } from '@/api/schema'

export type StateMinimumWage = components['schemas']['StateMinimumWageResponse']
export type CreateStateMinimumWage = components['schemas']['CreateStateMinimumWageRequest']
export type UpdateStateMinimumWage = components['schemas']['UpdateStateMinimumWageRequest']

export interface StateMinimumWageListParams {
  state?: string
  page: number
  pageSize: number
}

export const stateMinimumWageKeys = {
  all: ['stateMinimumWages'] as const,
  lists: () => [...stateMinimumWageKeys.all, 'list'] as const,
  list: (params: StateMinimumWageListParams) => [...stateMinimumWageKeys.lists(), params] as const,
  details: () => [...stateMinimumWageKeys.all, 'detail'] as const,
  detail: (id: number) => [...stateMinimumWageKeys.details(), id] as const,
}

export function useStateMinimumWages(params: StateMinimumWageListParams) {
  return useQuery({
    queryKey: stateMinimumWageKeys.list(params),
    queryFn: async () => {
      const { data, error } = await api.GET('/state-minimum-wages', {
        params: { query: { state: params.state || undefined, Page: params.page, PageSize: params.pageSize } },
      })
      if (error) {
        throw error
      }
      return data
    },
    placeholderData: (previous) => previous,
  })
}

export function useStateMinimumWage(id: number) {
  return useQuery({
    queryKey: stateMinimumWageKeys.detail(id),
    queryFn: async () => {
      const { data, error } = await api.GET('/state-minimum-wages/{id}', { params: { path: { id } } })
      if (error) {
        throw error
      }
      return data
    },
  })
}

export function useCreateStateMinimumWage() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: CreateStateMinimumWage) => {
      const { data, error } = await api.POST('/state-minimum-wages', { body })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: stateMinimumWageKeys.lists() }),
  })
}

export function useUpdateStateMinimumWage(id: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: UpdateStateMinimumWage) => {
      const { data, error } = await api.PUT('/state-minimum-wages/{id}', { params: { path: { id } }, body })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: stateMinimumWageKeys.detail(id) })
      void queryClient.invalidateQueries({ queryKey: stateMinimumWageKeys.lists() })
    },
  })
}

export function useDeleteStateMinimumWage() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: number) => {
      const { error } = await api.DELETE('/state-minimum-wages/{id}', { params: { path: { id } } })
      if (error) {
        throw error
      }
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: stateMinimumWageKeys.all }),
  })
}
