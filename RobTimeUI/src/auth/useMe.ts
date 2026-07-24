import { useQuery } from '@tanstack/react-query'
import { api } from '@/api/client'
import { useAuth } from './AuthProvider'
import type { Me } from '@/lib/permissions'

export const meQueryKey = ['me'] as const

/**
 * The signed-in user's identity and role, straight from `GET /me`. Everything permission-related
 * reads this rather than decoding the token client-side — the API is the authority on what a token
 * actually grants, and decoding claims in the browser invites them drifting apart.
 */
export function useMe() {
  const { isAuthenticated } = useAuth()

  return useQuery({
    queryKey: meQueryKey,
    enabled: isAuthenticated,
    queryFn: async (): Promise<Me> => {
      const { data, error } = await api.GET('/me')
      if (error) {
        throw new Error('Could not load the current user.')
      }
      return data
    },
  })
}
