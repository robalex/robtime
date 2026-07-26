import { createFileRoute, Outlet } from '@tanstack/react-router'

// Layout only — the pay rule editor lives in setup.payrules.$payRuleId.index.tsx, a sibling to
// the activate route under this same parent (same pattern as people.$employeeId.tsx). Needed once
// a child route exists: without a layout route rendering <Outlet/>, TanStack Router treats this
// file as the leaf for every path under /setup/payrules/$payRuleId, and child routes never render.
export const Route = createFileRoute('/setup/payrules/$payRuleId')({
  component: () => <Outlet />,
})
