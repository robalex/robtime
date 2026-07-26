import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { components } from '@/api/schema'

export type PayRule = components['schemas']['PayRuleResponse']
export type CreatePayRule = components['schemas']['CreatePayRuleRequest']
export type UpdatePayRule = components['schemas']['UpdatePayRuleRequest']
export type ActivatePayRule = components['schemas']['ActivatePayRuleRequest']
export type PayRuleTemplate = components['schemas']['PayRuleTemplateResponse']
export type WhatIfRequest = components['schemas']['WhatIfRequest']
export type WhatIfResponse = components['schemas']['WhatIfResponse']

export interface PayRuleListParams {
  clientId: number
  status?: components['schemas']['PayRuleStatus']
  page: number
  pageSize: number
}

export const payRuleKeys = {
  all: ['payRules'] as const,
  lists: () => [...payRuleKeys.all, 'list'] as const,
  list: (params: PayRuleListParams) => [...payRuleKeys.lists(), params] as const,
  details: () => [...payRuleKeys.all, 'detail'] as const,
  detail: (id: number) => [...payRuleKeys.details(), id] as const,
  templates: ['payRuleTemplates'] as const,
}

export function usePayRules(params: PayRuleListParams | null) {
  return useQuery({
    queryKey: params ? payRuleKeys.list(params) : [...payRuleKeys.lists(), 'none'],
    enabled: params !== null,
    queryFn: async () => {
      const { data, error } = await api.GET('/payrules', {
        params: {
          query: {
            clientId: params!.clientId,
            status: params!.status,
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

export function usePayRule(id: number) {
  return useQuery({
    queryKey: payRuleKeys.detail(id),
    queryFn: async () => {
      const { data, error } = await api.GET('/payrules/{id}', { params: { path: { id } } })
      if (error) {
        throw error
      }
      return data
    },
  })
}

// Static registry data (UI_PLAN.md §6 Rule 3) — fetched once and cached indefinitely; it only
// changes when the API itself is redeployed with new template presets.
export function usePayRuleTemplates() {
  return useQuery({
    queryKey: payRuleKeys.templates,
    queryFn: async () => {
      const { data, error } = await api.GET('/metadata/pay-rule-templates')
      if (error) {
        throw error
      }
      return data
    },
    staleTime: Infinity,
  })
}

export function useCreatePayRule() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: CreatePayRule) => {
      const { data, error } = await api.POST('/payrules', { body })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: payRuleKeys.lists() }),
  })
}

export function useUpdatePayRule(id: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: UpdatePayRule) => {
      const { data, error } = await api.PUT('/payrules/{id}', { params: { path: { id } }, body })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: payRuleKeys.detail(id) })
      void queryClient.invalidateQueries({ queryKey: payRuleKeys.lists() })
    },
  })
}

export function useDeletePayRule() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: number) => {
      const { error } = await api.DELETE('/payrules/{id}', { params: { path: { id } } })
      if (error) {
        throw error
      }
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: payRuleKeys.all }),
  })
}

// Promotes a Draft to Active, superseding the family's previous Active version if one exists
// (Gap F's versioning workflow).
export function useActivatePayRule(id: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: ActivatePayRule) => {
      const { data, error } = await api.POST('/payrules/{id}/activate', { params: { path: { id } }, body })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: payRuleKeys.all }),
  })
}

// Forks a new Draft from an Active/Superseded rule — the "create a new version instead of editing"
// workflow for a rule that's already been published.
export function useCreateNewPayRuleVersion(id: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async () => {
      const { data, error } = await api.POST('/payrules/{id}/versions', { params: { path: { id } } })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: payRuleKeys.all }),
  })
}

// Phase 4 §7's single-employee what-if — runs PayCalculator twice server-side (current assignment
// vs. this pay rule forced over the period) and returns a per-shift line-item diff. A mutation, not
// a query: it's a preview action the user triggers, not cacheable data keyed by fixed params.
export function useRunPayRuleWhatIf(id: number) {
  return useMutation({
    mutationFn: async (body: WhatIfRequest) => {
      const { data, error } = await api.POST('/payrules/{id}/what-if', { params: { path: { id } }, body })
      if (error) {
        throw error
      }
      return data
    },
  })
}
