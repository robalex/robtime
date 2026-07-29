import { useEffect, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Label } from '@/components/ui/label'
import { Select } from '@/components/ui/select'
import { usePositionAssignments } from '@/features/positionAssignments/queries'
import { useClockPunch, useClockStatus } from './queries'

/**
 * The self-service clock (UI_PLAN.md Phase 6.4). One state-aware button rather than a Clock In and a
 * Clock Out side by side: the UI only ever offers the transition that's actually valid, so punching
 * the wrong direction isn't reachable — worth more than the explicitness of showing both, especially
 * on a phone where a disabled twin is just a mis-tap target.
 */
export function ClockCard({ employeeId }: { employeeId: number }) {
  const status = useClockStatus(true)
  const punch = useClockPunch()
  const assignments = usePositionAssignments(employeeId)
  const [selectedPositionId, setSelectedPositionId] = useState<string>('')

  const isClockedIn = status.data?.isClockedIn ?? false
  const positions = assignments.data ?? []
  // Only offered when there's an actual choice to make — with one position (the common case) the
  // picker would be a control with nothing to choose, and with none the punch simply carries no
  // position and the engine falls back per PipelineContext.GetRateAt.
  const showPositionPicker = !isClockedIn && positions.length > 1

  const elapsed = useElapsed(status.data?.since ?? null)
  const activePositionName = positions.find(
    (a) => a.positionId === status.data?.positionId,
  )?.positionName

  function handlePunch() {
    punch.mutate({
      employeeId,
      kind: isClockedIn ? 'Out' : 'In',
      punchTime: new Date().toISOString(),
      positionId: showPositionPicker && selectedPositionId ? Number(selectedPositionId) : undefined,
    })
  }

  if (status.isPending) {
    return (
      <Card>
        <CardContent className="py-8 text-center text-sm text-muted-foreground">
          Loading your clock…
        </CardContent>
      </Card>
    )
  }

  if (status.isError) {
    return (
      <Card>
        <CardContent className="py-8 text-center text-sm text-destructive">
          Could not load your clock status. Refresh to try again.
        </CardContent>
      </Card>
    )
  }

  return (
    <Card>
      <CardContent className="flex flex-col items-center gap-6 py-8">
        <div className="space-y-1 text-center">
          <p className="text-lg font-medium">
            {isClockedIn ? `Clocked in since ${formatTime(status.data!.since!)}` : 'Clocked out'}
          </p>
          {isClockedIn && (
            <p className="text-sm text-muted-foreground">
              {[activePositionName, elapsed].filter(Boolean).join(' · ')}
            </p>
          )}
        </div>

        {showPositionPicker && (
          <div className="w-full max-w-xs space-y-2">
            <Label htmlFor="clock-position">Position</Label>
            <Select
              id="clock-position"
              value={selectedPositionId}
              onChange={(e) => setSelectedPositionId(e.target.value)}
            >
              <option value="">Select a position</option>
              {positions.map((a) => (
                <option key={a.id} value={String(a.positionId)}>
                  {a.positionName}
                </option>
              ))}
            </Select>
          </div>
        )}

        <Button
          size="lg"
          variant={isClockedIn ? 'destructive' : 'default'}
          className="h-16 w-full max-w-xs text-base"
          // A position must be chosen before clocking in when there's a genuine choice — otherwise
          // the punch silently lands on whichever assignment the engine resolves by date, which is
          // exactly the ambiguity the picker exists to remove.
          disabled={punch.isPending || (showPositionPicker && !selectedPositionId)}
          onClick={handlePunch}
        >
          {punch.isPending ? 'Saving…' : isClockedIn ? 'Clock Out' : 'Clock In'}
        </Button>

        {punch.isError && (
          <p className="text-sm text-destructive">
            That didn’t go through. Check your connection and try again.
          </p>
        )}
      </CardContent>
    </Card>
  )
}

function formatTime(instant: string): string {
  return new Date(instant).toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' })
}

/**
 * "4h 18m" since the open punch, ticking every 30s. Recomputed on an interval rather than once on
 * render because this screen is the kind a phone sits on for a whole shift — a static "0m elapsed"
 * would be wrong within a minute of opening it.
 */
function useElapsed(since: string | null): string | null {
  const [now, setNow] = useState(() => Date.now())

  useEffect(() => {
    if (!since) {
      return
    }
    const id = setInterval(() => setNow(Date.now()), 30_000)
    return () => clearInterval(id)
  }, [since])

  if (!since) {
    return null
  }

  const totalMinutes = Math.max(0, Math.floor((now - new Date(since).getTime()) / 60_000))
  const hours = Math.floor(totalMinutes / 60)
  const minutes = totalMinutes % 60
  return hours > 0 ? `${hours}h ${minutes}m elapsed` : `${minutes}m elapsed`
}
