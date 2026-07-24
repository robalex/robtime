import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/people')({
  component: People,
})

function People() {
  return (
    <div className="space-y-2">
      <h1 className="text-2xl font-semibold tracking-tight">People</h1>
      <p className="text-muted-foreground">Employee and position management lands in Phase 3.</p>
    </div>
  )
}
