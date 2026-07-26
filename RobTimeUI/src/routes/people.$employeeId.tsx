import { createFileRoute, Outlet } from '@tanstack/react-router'

// Layout only — the employee detail tabs live in people.$employeeId.index.tsx, siblings to
// positions/new and positions/$assignmentId under this same parent (same pattern as people.tsx).
// Needed once a child route exists: without a layout route rendering <Outlet/>, TanStack Router
// treats this file as the leaf for every path under /people/$employeeId, and child routes never render.
export const Route = createFileRoute('/people/$employeeId')({
  component: () => <Outlet />,
})
