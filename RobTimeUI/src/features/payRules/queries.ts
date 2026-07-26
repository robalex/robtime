import { useQuery } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { components } from '@/api/schema'

export type PayRule = components['schemas']['PayRuleResponse']

export interface PayRuleListParams {
  clientId: number
  status?: components['schemas']['PayRuleStatus']
  page: number
  pageSize: number
}

// The full pay rule editor (three-tier taxonomy, template picker, version history — UI_PLAN.md
// Phase 4) hasn't landed yet; this is only what the assignment picker needs to list existing rules.
export const payRuleKeys = {
  all: ['payRules'] as const,
  lists: () => [...payRuleKeys.all, 'list'] as const,
  list: (params: PayRuleListParams) => [...payRuleKeys.lists(), params] as const,
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
