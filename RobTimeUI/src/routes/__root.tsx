import { createRootRouteWithContext, Link, Outlet, useLocation, useNavigate } from '@tanstack/react-router'
import type { QueryClient } from '@tanstack/react-query'
import { useEffect } from 'react'
import { useAuth } from '@/auth/AuthProvider'
import { useMe } from '@/auth/useMe'
import { ClientSwitcher } from '@/features/clients/ClientSwitcher'
import { Button } from '@/components/ui/button'
import { can } from '@/lib/permissions'
import { cn } from '@/lib/utils'

// The four top-level destinations, no more (UI_PLAN.md §6 Rule 1). Nested config areas live *inside*
// Setup as a card grid, never as more top-level nav — the whole point of the four-destination rule.
//
// `visible` gates each one by role (added in Phase 6.4, the first slice where a real Employee-role
// user signs in — before that every account could reach everything, so an ungated array was
// harmless). Dashboard and Time are unconditional: everyone has a landing page, and Time is where an
// employee's own clock/timecard lives. Same caveat as lib/permissions.ts itself — this hides
// destinations that would render empty or 403, it does not secure them; the API is what enforces.
const NAV = [
  { to: '/', label: 'Dashboard', visible: () => true },
  { to: '/people', label: 'People', visible: can.viewPeople },
  { to: '/time', label: 'Time', visible: () => true },
  { to: '/setup', label: 'Setup', visible: can.viewSetup },
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
  const { isAuthenticated, signIn } = useAuth()
  const location = useLocation()
  const navigate = useNavigate()
  const isPublic = PUBLIC_ROUTES.includes(location.pathname)

  // Guarding here rather than in each route's `beforeLoad`: auth state lives in React context (the
  // token is deliberately in memory only), which router loaders can't read. One guard at the root
  // also means a new route is protected by default — the safer failure mode than opt-in protection,
  // where forgetting the guard silently exposes a screen.
  //
  // Goes straight to Cognito (signIn), not to our own /login page — this is what makes a page
  // refresh recover invisibly instead of needing a click. AuthProvider's own doc comment already
  // promises this: tokens are memory-only, so a refresh always drops isAuthenticated, but Cognito's
  // *own* session cookie means the round trip is silent when that cookie is still good — the whole
  // point falls apart if we stop at a local page requiring a click first. /login still exists as the
  // fallback for when signIn() itself can't even start (e.g. missing VITE_COGNITO_* env config) and
  // as the retry target auth/callback.tsx uses after a real failure (the user denied consent, etc.).
  useEffect(() => {
    if (!isAuthenticated && !isPublic) {
      // location.href, not location.pathname — the latter silently drops any search string (e.g.
      // ?tab=punches), so a reload on a non-default tab would round-trip through Cognito and land
      // back on the bare path. See auth.callback.tsx's matching use of `href` (not `to`) to actually
      // honour it on the way back.
      signIn(location.href).catch(() =>
        navigate({ to: '/login', search: { redirect: location.href }, replace: true }),
      )
    }
  }, [isAuthenticated, isPublic, location.href, navigate, signIn])

  if (isPublic) {
    return <Outlet />
  }

  if (!isAuthenticated) {
    // Covers both the instant before signIn()'s redirect actually takes effect and a genuinely
    // logged-out visit to Cognito's hosted login page and back — a blank frame here would otherwise
    // flash on every single navigation while that's in flight.
    return <p className="py-16 text-center text-muted-foreground">Redirecting to sign in…</p>
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
            {NAV.filter((item) => item.visible(me)).map((item) => (
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
