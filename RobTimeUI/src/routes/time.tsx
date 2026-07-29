import { createFileRoute } from '@tanstack/react-router'
import { useMe } from '@/auth/useMe'
import { can } from '@/lib/permissions'
import { Card, CardContent } from '@/components/ui/card'
import { ClockCard } from '@/features/clock/ClockCard'
import { Timecard } from '@/features/timecard/Timecard'
import { BulkPunchEntry } from '@/features/timecard/BulkPunchEntry'
import { PendingRequestsQueue } from '@/features/punchChangeRequests/PendingRequestsQueue'
import { MyPunchRequests } from '@/features/punchChangeRequests/MyPunchRequests'

export const Route = createFileRoute('/time')({
  component: Time,
})

/**
 * `/time` is deliberately first-person: my clock, my timecard (6.5), and the requests waiting on my
 * approval (6.6). Somebody *else's* timecard lives under People → that employee → Punches, beside
 * their positions and pay rule — see UI_PLAN.md's Phase 6 notes on why the split is by
 * whose-data-it-is rather than by role. <Timecard> itself is the same component mounted there.
 *
 * The queue is gated on `approvePunchChanges` (Supervisor+), not on having a linked employee record
 * — a supervisor with no employee row of their own still has requests waiting on them, so it renders
 * independently of the clock/timecard block above rather than nested inside it.
 */
function Time() {
  const { data: me } = useMe()

  return (
    <div className="space-y-6">
      <div className="space-y-2">
        <h1 className="text-2xl font-semibold tracking-tight">Time</h1>
        <p className="text-muted-foreground">
          {me?.employeeId
            ? 'Clock in and out, and review your own time.'
            : 'Your own time and approvals.'}
        </p>
      </div>

      {me?.employeeId ? (
        <>
          <ClockCard employeeId={me.employeeId} />
          <Timecard employeeId={me.employeeId} />
          <MyPunchRequests employeeId={me.employeeId} />
          <BulkPunchEntry employeeId={me.employeeId} />
        </>
      ) : (
        <Card>
          <CardContent className="py-8 text-center text-sm text-muted-foreground">
            No employee record is linked to this account, so there’s nothing to clock. An
            administrator can link one when creating your user.
          </CardContent>
        </Card>
      )}

      {can.approvePunchChanges(me) && <PendingRequestsQueue />}
    </div>
  )
}
