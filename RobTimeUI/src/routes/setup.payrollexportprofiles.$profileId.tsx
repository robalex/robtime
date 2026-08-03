import { createFileRoute, Outlet } from '@tanstack/react-router'

// Layout only — the profile detail tabs live in setup.payrollexportprofiles.$profileId.index.tsx,
// siblings to earningcodes/new, identifiers/new, and exports/new under this same parent (same
// pattern as people.$employeeId.tsx). Needed once a child route exists: without a layout route
// rendering <Outlet/>, TanStack Router treats this file as the leaf for every path under
// /setup/payrollexportprofiles/$profileId, and child routes never render.
export const Route = createFileRoute('/setup/payrollexportprofiles/$profileId')({
  component: () => <Outlet />,
})
