import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/time')({
  component: Time,
})

function Time() {
  return (
    <div className="space-y-2">
      <h1 className="text-2xl font-semibold tracking-tight">Time</h1>
      <p className="text-muted-foreground">
        Self-service clock and timecards land in Phase 6.
      </p>
    </div>
  )
}
