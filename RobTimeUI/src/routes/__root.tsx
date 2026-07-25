import { createRootRouteWithContext, Link, Outlet, useLocation, useNavigate } from '@tanstack/react-router'
import type { QueryClient } from '@tanstack/react-query'
import { useEffect } from 'react'
import { useAuth } from '@/auth/AuthProvider'
import { useMe } from '@/auth/useMe'
import { ClientSwitcher } from '@/features/clients/ClientSwitcher'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

// The four top-level destinations, no more (UI_PLAN.md §6 Rule 1). Nested config areas live *inside*
// Setup as a card grid, never as more top-level nav — the whole point of the four-destination rule.
const NAV = [
  { to: '/', label: 'Dashboard' },
  { to: '/people', label: 'People' },
  { to: '/time', label: 'Time' },
  { to: '/setup', label: 'Setup' },
] as const

// Reachable without a session. Everything else redirects to /login.
const PUBLIC_ROUTES = ['/login', '/auth/callback']

interface RouterContext {
  queryClient: QueryClient
}

export const Route = createRootRouteWithContext<RouterContext>()({
  component: RootLayout,
})

function RootLayout() {
  const { isAuthenticated } = useAuth()
  const location = useLocation()
  const navigate = useNavigate()
  const isPublic = PUBLIC_ROUTES.includes(location.pathname)

  // Guarding here rather than in each route's `beforeLoad`: auth state lives in React context (the
  // token is deliberately in memory only), which router loaders can't read. One guard at the root
  // also means a new route is protected by default — the safer failure mode than opt-in protection,
  // where forgetting the guard silently exposes a screen.
  useEffect(() => {
    if (!isAuthenticated && !isPublic) {
      navigate({ to: '/login', search: { redirect: location.pathname }, replace: true })
    }
  }, [isAuthenticated, isPublic, location.pathname, navigate])

  if (isPublic) {
    return <Outlet />
  }

  if (!isAuthenticated) {
    return null // redirecting
  }

  return <AuthenticatedLayout />
}

function AuthenticatedLayout() {
  const { signOut } = useAuth()
  const { data: me, isLoading, isError } = useMe()

  if (isLoading) {
    return <p className="py-16 text-center text-muted-foreground">Loading…</p>
  }

  if (isError) {
    return (
      <div className="mx-auto max-w-md space-y-4 py-16 text-center">
        <h1 className="text-xl font-semibold">Could not load your account</h1>
        <p className="text-sm text-muted-foreground">
          You are signed in, but the API did not accept the session.
        </p>
        <Button variant="outline" onClick={signOut}>
          Sign out
        </Button>
      </div>
    )
  }

  // Authenticated against Cognito but with no AppUser row — the bootstrap admin, or anyone created
  // in the Cognito console rather than through POST /users. Saying so beats rendering an app whose
  // every query silently returns nothing because there's no tenant.
  if (me && !me.isProvisioned) {
    return (
      <div className="mx-auto max-w-md space-y-4 py-16 text-center">
        <h1 className="text-xl font-semibold">Account not set up</h1>
        <p className="text-sm text-muted-foreground">
          You signed in as {me.email ?? me.cognitoSub}, but this account has no RobTime profile yet.
          An administrator needs to provision it.
        </p>
        <Button variant="outline" onClick={signOut}>
          Sign out
        </Button>
      </div>
    )
  }

  // A selection pointing at a client that no longer exists (deleted, or a stale sessionStorage value
  // from a previous environment). Without calling this out, every screen would render empty and be
  // indistinguishable from "no data yet" — which is precisely why /me returns the resolved name.
  const staleSelection = me?.clientId != null && me.clientName == null

  return (
    <div className="min-h-svh">
      <header className="border-b">
        <div className="mx-auto flex h-14 max-w-6xl items-center gap-6 px-4">
          <span className="font-semibold tracking-tight">RobTime</span>
          <nav className="flex items-center gap-1">
            {NAV.map((item) => (
              <Link
                key={item.to}
                to={item.to}
                // exact match for the Dashboard root so it isn't perpetually "active"
                activeOptions={{ exact: item.to === '/' }}
                className={cn(
                  'rounded-md px-3 py-1.5 text-sm font-medium text-muted-foreground transition-colors hover:text-foreground',
                )}
                activeProps={{ className: 'bg-secondary text-foreground' }}
              >
                {item.label}
              </Link>
            ))}
          </nav>
          <div className="ml-auto flex items-center gap-3">
            {/* Only SystemAdmin can be scoped into different clients; every other role has exactly
                one, so a switcher would be a control with nothing to choose. */}
            {me && me.role === 'SystemAdmin' && <ClientSwitcher me={me} />}
            <span className="text-sm text-muted-foreground">
              {me?.displayName ?? me?.email}
              {me?.role && <span className="ml-2 text-xs">({me.role})</span>}
            </span>
            <Button variant="ghost" size="sm" onClick={signOut}>
              Sign out
            </Button>
          </div>
        </div>
      </header>
      {staleSelection && (
        <div className="border-b bg-destructive/10">
          <p className="mx-auto max-w-6xl px-4 py-2 text-sm text-destructive">
            The selected client no longer exists. Pick another from the Client menu above.
          </p>
        </div>
      )}
      <main className="mx-auto max-w-6xl px-4 py-8">
        <Outlet />
      </main>
    </div>
  )
}
