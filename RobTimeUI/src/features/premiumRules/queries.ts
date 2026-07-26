import { useQuery } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { components } from '@/api/schema'

export type PremiumRuleMetadata = components['schemas']['PremiumRuleMetadataResponse']

// Static, code-registered metadata (PremiumRegistry) — fetched once and cached indefinitely, same
// reasoning as usePayRuleTemplates.
export function usePremiumRules() {
  return useQuery({
    queryKey: ['premiumRules'],
    queryFn: async () => {
      const { data, error } = await api.GET('/metadata/premium-rules')
      if (error) {
        throw error
      }
      return data
    },
    staleTime: Infinity,
  })
}
