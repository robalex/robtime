import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { components } from '@/api/schema'

export type PositionAssignment = components['schemas']['PositionAssignmentResponse']
export type CreatePositionAssignment = components['schemas']['CreatePositionAssignmentRequest']
export type UpdatePositionAssignment = components['schemas']['UpdatePositionAssignmentRequest']

// Keyed by employeeId only — an assignment has no meaning apart from its employee, so there's no
// separate "list params" shape the way Employee/Position have (client, search, page).
export const positionAssignmentKeys = {
  all: ['positionAssignments'] as const,
  lists: () => [...positionAssignmentKeys.all, 'list'] as const,
  list: (employeeId: number) => [...positionAssignmentKeys.lists(), employeeId] as const,
}

export function usePositionAssignments(employeeId: number) {
  return useQuery({
    queryKey: positionAssignmentKeys.list(employeeId),
    queryFn: async () => {
      const { data, error } = await api.GET('/employees/{employeeId}/positions', {
        params: { path: { employeeId } },
      })
      if (error) {
        throw error
      }
      return data
    },
  })
}

export function useCreatePositionAssignment(employeeId: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: CreatePositionAssignment) => {
      const { data, error } = await api.POST('/employees/{employeeId}/positions', {
        params: { path: { employeeId } },
        body,
      })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: positionAssignmentKeys.list(employeeId) }),
  })
}

export function useUpdatePositionAssignment(employeeId: number, id: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: UpdatePositionAssignment) => {
      const { data, error } = await api.PUT('/employees/{employeeId}/positions/{id}', {
        params: { path: { employeeId, id } },
        body,
      })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: positionAssignmentKeys.list(employeeId) }),
  })
}

export function useDeletePositionAssignment(employeeId: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: number) => {
      const { error } = await api.DELETE('/employees/{employeeId}/positions/{id}', {
        params: { path: { employeeId, id } },
      })
      if (error) {
        throw error
      }
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: positionAssignmentKeys.list(employeeId) }),
  })
}
