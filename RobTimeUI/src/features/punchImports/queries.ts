import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { components } from '@/api/schema'

export type PunchImportBatch = components['schemas']['PunchImportBatchResponse']

export const punchImportKeys = {
  all: ['punchImports'] as const,
  list: () => [...punchImportKeys.all, 'list'] as const,
}

export function usePunchImportBatches() {
  return useQuery({
    queryKey: punchImportKeys.list(),
    queryFn: async () => {
      const { data, error } = await api.GET('/punch-imports', {
        params: { query: { Page: 1, PageSize: 50 } },
      })
      if (error) {
        throw error
      }
      return data
    },
  })
}

export function useImportPunches() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (file: File) => {
      const formData = new FormData()
      formData.append('file', file)
      const { data, error } = await api.POST('/punch-imports', {
        // openapi-typescript models IFormFile as a bare `string` (format: binary) in the generated
        // schema, so there's no request-body type that actually matches a real FormData/File — the
        // cast is required, not a shortcut around one. openapi-fetch passes a FormData instance
        // through to fetch() untouched rather than re-serializing it (see its defaultBodySerializer:
        // "if body instanceof FormData, return body as-is"), so this is safe on the wire despite the
        // type mismatch.
        body: formData as never,
      })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: punchImportKeys.list() })
    },
  })
}

export function useDeletePunchImportBatch() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: number) => {
      const { data, error } = await api.DELETE('/punch-imports/{id}', { params: { path: { id } } })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: punchImportKeys.list() })
    },
  })
}
