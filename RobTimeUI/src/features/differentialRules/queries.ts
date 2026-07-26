import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { components } from '@/api/schema'

export type DifferentialRule = components['schemas']['DifferentialRuleResponse']
export type CreateDifferentialRule = components['schemas']['CreateDifferentialRuleRequest']
export type UpdateDifferentialRule = components['schemas']['UpdateDifferentialRuleRequest']

export interface DifferentialRuleListParams {
  clientId: number
  search?: string
  page: number
  pageSize: number
}

export const differentialRuleKeys = {
  all: ['differentialRules'] as const,
  lists: () => [...differentialRuleKeys.all, 'list'] as const,
  list: (params: DifferentialRuleListParams) => [...differentialRuleKeys.lists(), params] as const,
  details: () => [...differentialRuleKeys.all, 'detail'] as const,
  detail: (id: number) => [...differentialRuleKeys.details(), id] as const,
}

export function useDifferentialRules(params: DifferentialRuleListParams | null) {
  return useQuery({
    queryKey: params ? differentialRuleKeys.list(params) : [...differentialRuleKeys.lists(), 'none'],
    enabled: params !== null,
    queryFn: async () => {
      const { data, error } = await api.GET('/differentialrules', {
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

export function useDifferentialRule(id: number) {
  return useQuery({
    queryKey: differentialRuleKeys.detail(id),
    queryFn: async () => {
      const { data, error } = await api.GET('/differentialrules/{id}', { params: { path: { id } } })
      if (error) {
        throw error
      }
      return data
    },
  })
}

export function useCreateDifferentialRule() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: CreateDifferentialRule) => {
      const { data, error } = await api.POST('/differentialrules', { body })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: differentialRuleKeys.lists() }),
  })
}

export function useUpdateDifferentialRule(id: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: UpdateDifferentialRule) => {
      const { data, error } = await api.PUT('/differentialrules/{id}', { params: { path: { id } }, body })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: differentialRuleKeys.detail(id) })
      void queryClient.invalidateQueries({ queryKey: differentialRuleKeys.lists() })
    },
  })
}

export function useDeleteDifferentialRule() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: number) => {
      const { error } = await api.DELETE('/differentialrules/{id}', { params: { path: { id } } })
      if (error) {
        throw error
      }
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: differentialRuleKeys.all }),
  })
}
