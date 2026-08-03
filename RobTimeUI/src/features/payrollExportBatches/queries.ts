import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { components } from '@/api/schema'
import { downloadBlob } from '@/lib/download'

export type PayrollExportBatch = components['schemas']['PayrollExportBatchResponse']
export type CreatePayrollExport = components['schemas']['CreatePayrollExportRequest']

// A batch has no meaning apart from the profile it's for — same parent-scoped shape as the other two
// child features.
export const payrollExportBatchKeys = {
  all: ['payrollExportBatches'] as const,
  list: (profileId: number) => [...payrollExportBatchKeys.all, 'list', profileId] as const,
}

export function usePayrollExportBatches(profileId: number) {
  return useQuery({
    queryKey: payrollExportBatchKeys.list(profileId),
    queryFn: async () => {
      const { data, error } = await api.GET('/payroll-export-profiles/{profileId}/exports', {
        // A batch is one row per export run, not per employee — a flat page covers every real
        // client's history without needing page controls, same simplification as
        // usePunchImportBatches().
        params: { path: { profileId }, query: { Page: 1, PageSize: 50 } },
      })
      if (error) {
        throw error
      }
      return data
    },
  })
}

export function useCreatePayrollExportBatch(profileId: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: CreatePayrollExport) => {
      const { data, error } = await api.POST('/payroll-export-profiles/{profileId}/exports', {
        params: { path: { profileId } },
        body,
      })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: payrollExportBatchKeys.list(profileId) }),
  })
}

export function useVoidPayrollExportBatch(profileId: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: number) => {
      const { data, error } = await api.POST('/payroll-export-profiles/{profileId}/exports/{id}/void', {
        params: { path: { profileId, id } },
      })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: payrollExportBatchKeys.list(profileId) }),
  })
}

export function useDownloadPayrollExportBatch(profileId: number) {
  return useMutation({
    mutationFn: async ({ id, fileName }: { id: number; fileName: string }) => {
      // The OpenAPI doc has no typed content schema for this binary response (TypedResults.File
      // isn't schema-describable), so `data`/`error` come back untyped here — parseAs:'blob' is the
      // only way to get raw bytes through the same authenticated client (needed so the bearer token
      // middleware still attaches; a bare <a href> to the endpoint would not carry it). Branch on the
      // raw response status instead of the typed `error` union, which this operation doesn't have.
      const { data, response } = await api.GET('/payroll-export-profiles/{profileId}/exports/{id}/download', {
        params: { path: { profileId, id } },
        parseAs: 'blob',
      })
      if (!response.ok) {
        // A non-2xx body here is still the blob-parsed bytes of a problem+json payload, not the
        // parsed object toApiProblem expects — read and re-parse it by hand.
        const text = await (data as Blob).text()
        let problem: unknown
        try {
          problem = JSON.parse(text)
        } catch {
          throw new Error(`Could not download this export (status ${response.status}).`)
        }
        throw problem
      }
      downloadBlob(data as Blob, fileName)
    },
  })
}
