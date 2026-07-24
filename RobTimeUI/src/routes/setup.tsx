import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/setup')({
  component: Setup,
})

function Setup() {
  return (
    <div className="space-y-2">
      <h1 className="text-2xl font-semibold tracking-tight">Setup</h1>
      <p className="text-muted-foreground">
        A card grid of config areas (UI_PLAN.md §6 Rule 1), not a menu. Clients CRUD lands here as
        the reference pattern in the next slice.
      </p>
    </div>
  )
}
