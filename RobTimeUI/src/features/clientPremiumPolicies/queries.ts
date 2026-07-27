import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { components } from '@/api/schema'

export type ClientPremiumPolicy = components['schemas']['ClientPremiumPolicyResponse']
export type CreateClientPremiumPolicy = components['schemas']['CreateClientPremiumPolicyRequest']
export type UpdateClientPremiumPolicy = components['schemas']['UpdateClientPremiumPolicyRequest']

export interface ClientPremiumPolicyListParams {
  clientId: number
  premiumCode?: string
  page: number
  pageSize: number
}

export const clientPremiumPolicyKeys = {
  all: ['clientPremiumPolicies'] as const,
  lists: () => [...clientPremiumPolicyKeys.all, 'list'] as const,
  list: (params: ClientPremiumPolicyListParams) => [...clientPremiumPolicyKeys.lists(), params] as const,
  details: () => [...clientPremiumPolicyKeys.all, 'detail'] as const,
  detail: (id: number) => [...clientPremiumPolicyKeys.details(), id] as const,
}

export function useClientPremiumPolicies(params: ClientPremiumPolicyListParams | null) {
  return useQuery({
    queryKey: params ? clientPremiumPolicyKeys.list(params) : [...clientPremiumPolicyKeys.lists(), 'none'],
    enabled: params !== null,
    queryFn: async () => {
      const { data, error } = await api.GET('/clientpremiumpolicies', {
        params: {
          query: {
            clientId: params!.clientId,
            premiumCode: params!.premiumCode || undefined,
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

export function useClientPremiumPolicy(id: number) {
  return useQuery({
    queryKey: clientPremiumPolicyKeys.detail(id),
    queryFn: async () => {
      const { data, error } = await api.GET('/clientpremiumpolicies/{id}', { params: { path: { id } } })
      if (error) {
        throw error
      }
      return data
    },
  })
}

export function useCreateClientPremiumPolicy() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: CreateClientPremiumPolicy) => {
      const { data, error } = await api.POST('/clientpremiumpolicies', { body })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: clientPremiumPolicyKeys.lists() }),
  })
}

export function useUpdateClientPremiumPolicy(id: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: UpdateClientPremiumPolicy) => {
      const { data, error } = await api.PUT('/clientpremiumpolicies/{id}', { params: { path: { id } }, body })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: clientPremiumPolicyKeys.detail(id) })
      void queryClient.invalidateQueries({ queryKey: clientPremiumPolicyKeys.lists() })
    },
  })
}

export function useDeleteClientPremiumPolicy() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: number) => {
      const { error } = await api.DELETE('/clientpremiumpolicies/{id}', { params: { path: { id } } })
      if (error) {
        throw error
      }
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: clientPremiumPolicyKeys.all }),
  })
}
