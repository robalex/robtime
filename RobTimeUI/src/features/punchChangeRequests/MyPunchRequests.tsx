import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { useMe } from '@/auth/useMe'
import { usePositions } from '@/features/positions/queries'
import { toApiProblem } from '@/lib/problem'
import { cn } from '@/lib/utils'
import { ChangeSummary } from './ChangeSummary'
import { formatInstant } from './formatting'
import { useMyPunchChangeRequests, type PunchChangeRequest } from './queries'

const STATUS_STYLES: Record<PunchChangeRequest['status'], string> = {
  Pending: 'bg-amber-600/10 text-amber-800 dark:text-amber-400',
  Approved: 'bg-emerald-600/10 text-emerald-800 dark:text-emerald-400',
  Denied: 'bg-destructive/10 text-destructive',
}

/**
 * The other half of self-service punch requests: submitting one via Timecard's inline forms with no
 * way to check what happened to it isn't a finished feature. Shows the signed-in employee's own
 * requests (any status) so approval/denial is visible without having to ask a supervisor. Reuses
 * ChangeSummary — same rendering PendingRequestsQueue uses for the reviewer's side of the same data.
 */
export function MyPunchRequests({ employeeId }: { employeeId: number }) {
  const { data: me } = useMe()
  const { data, isPending, isError, error } = useMyPunchChangeRequests(employeeId, true)
  const positions = usePositions(
    me?.clientId ? { clientId: me.clientId, page: 1, pageSize: 200 } : null,
  )
  const positionNames = new Map((positions.data?.items ?? []).map((p) => [p.id, p.name]))

  if (isPending) {
    return <p className="text-sm text-muted-foreground">Loading your requests…</p>
  }

  if (isError) {
    return (
      <p className="text-sm text-destructive">
        {toApiProblem(error, 'Could not load your punch requests.').message}
      </p>
    )
  }

  const requests = data.items
  if (requests.length === 0) {
    return null
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">My punch requests</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        {requests.map((request) => (
          <div key={request.id} className="space-y-2 rounded-md border p-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <span className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
                {request.changeKind}
              </span>
              <div className="flex items-center gap-2">
                <span className="text-xs text-muted-foreground">{formatInstant(request.createdAt)}</span>
                <span
                  className={cn('rounded-full px-2 py-0.5 text-xs font-medium', STATUS_STYLES[request.status])}
                >
                  {request.status}
                </span>
              </div>
            </div>
            <ChangeSummary request={request} positionNames={positionNames} />
            <p className="text-sm">
              <span className="text-muted-foreground">Reason: </span>
              {request.reason}
            </p>
            {request.status !== 'Pending' && request.reviewNote && (
              <p className="text-sm">
                <span className="text-muted-foreground">Reviewer note: </span>
                {request.reviewNote}
              </p>
            )}
          </div>
        ))}
      </CardContent>
    </Card>
  )
}
