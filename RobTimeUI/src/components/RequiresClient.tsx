import type { ReactNode } from 'react'
import { useMe } from '@/auth/useMe'

/**
 * Renders children only once a client is in scope, and explains the gap otherwise.
 *
 * Only a SystemAdmin can be in the "no client" state (every other role's client comes from its
 * token), but the message is written for whoever hits it — the alternative is a screen full of empty
 * tables that looks like missing data rather than missing scope. Shared because every tenant-scoped
 * screen from Phase 3 onward needs exactly this.
 */
export function RequiresClient({ children }: { children: (clientId: number) => ReactNode }) {
  const { data: me, isPending } = useMe()

  if (isPending) {
    return <p className="text-sm text-muted-foreground">Loading…</p>
  }

  if (me?.clientId == null) {
    return (
      <div className="rounded-lg border border-dashed p-8 text-center">
        <p className="font-medium">No client selected</p>
        <p className="mt-1 text-sm text-muted-foreground">
          Choose a client from the menu in the header to see its people and configuration.
        </p>
      </div>
    )
  }

  return <>{children(me.clientId)}</>
}
