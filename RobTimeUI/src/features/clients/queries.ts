import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { components } from '@/api/schema'

export type Client = components['schemas']['ClientResponse']
export type CreateClient = components['schemas']['CreateClientRequest']
export type UpdateClient = components['schemas']['UpdateClientRequest']

export interface ClientListParams {
  search?: string
  page: number
  pageSize: number
}

/**
 * Query keys are hierarchical so a mutation can invalidate the right slice: `['clients']`
 * invalidates every list page and every detail; `['clients', 'list', params]` is one specific page.
 * This is the shape the other entity features copy.
 */
export const clientKeys = {
  all: ['clients'] as const,
  lists: () => [...clientKeys.all, 'list'] as const,
  list: (params: ClientListParams) => [...clientKeys.lists(), params] as const,
  details: () => [...clientKeys.all, 'detail'] as const,
  detail: (id: number) => [...clientKeys.details(), id] as const,
}

export function useClients(params: ClientListParams) {
  return useQuery({
    queryKey: clientKeys.list(params),
    queryFn: async () => {
      const { data, error } = await api.GET('/clients', {
        params: {
          query: {
            search: params.search || undefined,
            // Capitalised because the API binds these via [AsParameters] PagingQuery, which matches
            // the C# property names — the generated types carry that through faithfully.
            Page: params.page,
            PageSize: params.pageSize,
          },
        },
      })
      if (error) {
        throw error
      }
      return data
    },
    // Keeps the previous page visible while the next one loads, so paging and typing in the search
    // box don't flash an empty table between results.
    placeholderData: (previous) => previous,
  })
}

export function useClient(id: number) {
  return useQuery({
    queryKey: clientKeys.detail(id),
    queryFn: async () => {
      const { data, error } = await api.GET('/clients/{id}', { params: { path: { id } } })
      if (error) {
        throw error
      }
      return data
    },
  })
}

export function useCreateClient() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: CreateClient) => {
      const { data, error } = await api.POST('/clients', { body })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: clientKeys.lists() }),
  })
}

export function useUpdateClient(id: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: UpdateClient) => {
      const { data, error } = await api.PUT('/clients/{id}', { params: { path: { id } }, body })
      if (error) {
        throw error
      }
      return data
    },

    // Optimistic update: write the new name into the cache before the server confirms, so the
    // rename feels instant. onMutate's return value becomes `context` in onError, which is how the
    // previous value gets restored if the request fails.
    onMutate: async (body) => {
      await queryClient.cancelQueries({ queryKey: clientKeys.detail(id) })
      const previous = queryClient.getQueryData<Client>(clientKeys.detail(id))
      if (previous) {
        queryClient.setQueryData<Client>(clientKeys.detail(id), { ...previous, name: body.name })
      }
      return { previous }
    },
    onError: (_error, _body, context) => {
      if (context?.previous) {
        queryClient.setQueryData(clientKeys.detail(id), context.previous)
      }
    },
    // Refetch regardless of outcome: on success to pick up anything the server changed that we
    // didn't predict, on failure to be certain the cache matches reality rather than our rollback.
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: clientKeys.detail(id) })
      void queryClient.invalidateQueries({ queryKey: clientKeys.lists() })
    },
  })
}

export function useDeleteClient() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: number) => {
      const { error } = await api.DELETE('/clients/{id}', { params: { path: { id } } })
      if (error) {
        throw error
      }
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: clientKeys.all }),
  })
}
