import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RouterProvider, createRouter } from '@tanstack/react-router'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthProvider } from './auth/AuthProvider'
import { routeTree } from './routeTree.gen'
import './index.css'

// One QueryClient for the app. Defaults tuned for a read-heavy config tool (UI_PLAN.md §2): data is
// not refetched on every window focus (config changes are deliberate, not ambient), and a short
// staleTime avoids a refetch storm when navigating between the list and detail of the same entity.
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      refetchOnWindowFocus: false,
    },
  },
})

// The router is given the QueryClient via context so route loaders/actions can prefetch and
// invalidate without importing the singleton directly.
const router = createRouter({
  routeTree,
  context: { queryClient },
  defaultPreload: 'intent',
})

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      {/* Outside the router: the root route reads auth state to guard every child route. */}
      <AuthProvider>
        <RouterProvider router={router} />
      </AuthProvider>
    </QueryClientProvider>
  </StrictMode>,
)
