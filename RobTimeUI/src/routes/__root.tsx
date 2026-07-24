import { createRootRouteWithContext, Link, Outlet } from '@tanstack/react-router'
import type { QueryClient } from '@tanstack/react-query'
import { cn } from '@/lib/utils'

// The four top-level destinations, no more (UI_PLAN.md §6 Rule 1). Nested config areas live *inside*
// Setup as a card grid, never as more top-level nav — the whole point of the four-destination rule.
const NAV = [
  { to: '/', label: 'Dashboard' },
  { to: '/people', label: 'People' },
  { to: '/time', label: 'Time' },
  { to: '/setup', label: 'Setup' },
] as const

interface RouterContext {
  queryClient: QueryClient
}

export const Route = createRootRouteWithContext<RouterContext>()({
  component: RootLayout,
})

function RootLayout() {
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
        </div>
      </header>
      <main className="mx-auto max-w-6xl px-4 py-8">
        <Outlet />
      </main>
    </div>
  )
}
