import { createFileRoute, Outlet } from '@tanstack/react-router'

// Layout only — People lands on the employee list (people.index.tsx), the most-visited screen in any
// time-and-attendance system (§6 Rule 1).
export const Route = createFileRoute('/people')({
  component: () => <Outlet />,
})
