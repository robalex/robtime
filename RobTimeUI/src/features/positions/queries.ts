import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { components } from '@/api/schema'

export type Position = components['schemas']['PositionResponse']
export type CreatePosition = components['schemas']['CreatePositionRequest']
export type UpdatePosition = components['schemas']['UpdatePositionRequest']

export interface PositionListParams {
  clientId: number
  search?: string
  page: number
  pageSize: number
}

export const positionKeys = {
  all: ['positions'] as const,
  lists: () => [...positionKeys.all, 'list'] as const,
  list: (params: PositionListParams) => [...positionKeys.lists(), params] as const,
  details: () => [...positionKeys.all, 'detail'] as const,
  detail: (id: number) => [...positionKeys.details(), id] as const,
}

export function usePositions(params: PositionListParams | null) {
  return useQuery({
    queryKey: params ? positionKeys.list(params) : [...positionKeys.lists(), 'none'],
    enabled: params !== null,
    queryFn: async () => {
      const { data, error } = await api.GET('/positions', {
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

export function usePosition(id: number) {
  return useQuery({
    queryKey: positionKeys.detail(id),
    queryFn: async () => {
      const { data, error } = await api.GET('/positions/{id}', { params: { path: { id } } })
      if (error) {
        throw error
      }
      return data
    },
  })
}

export function useCreatePosition() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: CreatePosition) => {
      const { data, error } = await api.POST('/positions', { body })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: positionKeys.lists() }),
  })
}

export function useUpdatePosition(id: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: UpdatePosition) => {
      const { data, error } = await api.PUT('/positions/{id}', { params: { path: { id } }, body })
      if (error) {
        throw error
      }
      return data
    },
    onMutate: async (body) => {
      await queryClient.cancelQueries({ queryKey: positionKeys.detail(id) })
      const previous = queryClient.getQueryData<Position>(positionKeys.detail(id))
      if (previous) {
        queryClient.setQueryData<Position>(positionKeys.detail(id), { ...previous, ...body })
      }
      return { previous }
    },
    onError: (_error, _body, context) => {
      if (context?.previous) {
        queryClient.setQueryData(positionKeys.detail(id), context.previous)
      }
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: positionKeys.detail(id) })
      void queryClient.invalidateQueries({ queryKey: positionKeys.lists() })
    },
  })
}

export function useDeletePosition() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: number) => {
      const { error } = await api.DELETE('/positions/{id}', { params: { path: { id } } })
      if (error) {
        throw error
      }
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: positionKeys.all }),
  })
}
