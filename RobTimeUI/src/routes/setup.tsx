import { createFileRoute, Outlet } from '@tanstack/react-router'

// Layout route only — the actual Setup home (the card grid) is setup.index.tsx, and the config areas
// are nested children. Keeping this a bare Outlet means adding an area is a new file, not an edit
// here.
export const Route = createFileRoute('/setup')({
  component: () => <Outlet />,
})
