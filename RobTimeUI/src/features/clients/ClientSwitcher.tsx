import { useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { setSelectedClientId } from '@/auth/clientSelection'
import { useClients } from './queries'
import type { Me } from '@/lib/permissions'

/**
 * Lets a SystemAdmin choose which client they're working inside (UI_PLAN.md §5). Rendered only for
 * that role — every other role has exactly one client, baked into their token, and nothing to switch
 * between.
 */
export function ClientSwitcher({ me }: { me: Me }) {
  const queryClient = useQueryClient()
  const navigate = useNavigate()

  // A SystemAdmin can list every client; this is the same endpoint the Clients screen uses, so the
  // switcher usually renders from cache.
  const { data } = useClients({ page: 1, pageSize: 100 })

  const switchTo = async (value: string) => {
    setSelectedClientId(value === '' ? null : Number(value))

    // Load-bearing, not housekeeping: every cached query was fetched under the *previous* tenant.
    // Keeping the cache across a switch would render one client's employees while scoped to
    // another — a cross-tenant leak in the UI even though the API answered each request correctly.
    // Clearing is the blunt, obviously-correct option; selective invalidation here would be an
    // optimisation with a data-leak failure mode.
    queryClient.clear()

    // Back to a neutral screen: the current route may be a detail page belonging to the client we
    // just switched away from, which would otherwise 404 immediately after switching.
    await navigate({ to: '/' })
  }

  return (
    <label className="flex items-center gap-2 text-sm">
      <span className="text-muted-foreground">Client</span>
      <select
        className="h-8 rounded-md border border-input bg-background px-2 text-sm"
        value={me.clientId ?? ''}
        onChange={(event) => void switchTo(event.target.value)}
      >
        <option value="">Select a client…</option>
        {data?.items.map((client) => (
          <option key={client.id} value={client.id}>
            {client.name}
          </option>
        ))}
      </select>
    </label>
  )
}
