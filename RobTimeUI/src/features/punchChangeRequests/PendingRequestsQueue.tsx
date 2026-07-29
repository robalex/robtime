import { useState } from 'react'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { useMe } from '@/auth/useMe'
import { usePositions } from '@/features/positions/queries'
import { toApiProblem } from '@/lib/problem'
import { ChangeSummary } from './ChangeSummary'
import { formatInstant } from './formatting'
import {
  useDecidePunchChangeRequest,
  usePendingPunchChangeRequests,
  type PunchChangeRequest,
} from './queries'

/**
 * A supervisor's inbox of pending punch-change requests — UI_PLAN.md's Phase 6.6, built once and
 * mounted on `/time` (it's *my* work waiting on me, not a view of any one employee's data, so it
 * doesn't belong under People). Approve/deny reuses the same PunchChangeRequestService.DecideAsync
 * path a direct API call already exercises; this component is the review surface over it.
 */
export function PendingRequestsQueue() {
  const { data: me } = useMe()
  const { data, isPending, isError, error } = usePendingPunchChangeRequests()
  // Best-effort position-name lookup, not a hard dependency — falls back to "Position #N" below if
  // it hasn't loaded or the caller has no clientId (a SystemAdmin with none selected).
  const positions = usePositions(
    me?.clientId ? { clientId: me.clientId, page: 1, pageSize: 200 } : null,
  )
  const positionNames = new Map((positions.data?.items ?? []).map((p) => [p.id, p.name]))

  if (isPending) {
    return <p className="text-sm text-muted-foreground">Loading requests…</p>
  }

  if (isError) {
    return (
      <p className="text-sm text-destructive">
        {toApiProblem(error, 'Could not load pending requests.').message}
      </p>
    )
  }

  const requests = data.items

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">
          {requests.length === 0
            ? 'No requests pending'
            : `${requests.length} ${requests.length === 1 ? 'request' : 'requests'} pending`}
        </CardTitle>
      </CardHeader>
      {requests.length > 0 && (
        <CardContent className="space-y-4">
          {requests.map((request) => (
            <RequestRow key={request.id} request={request} positionNames={positionNames} />
          ))}
        </CardContent>
      )}
    </Card>
  )
}

function RequestRow({
  request,
  positionNames,
}: {
  request: PunchChangeRequest
  positionNames: Map<number, string>
}) {
  const decide = useDecidePunchChangeRequest()
  const [note, setNote] = useState('')
  const [decideError, setDecideError] = useState<string | null>(null)

  const employeeName =
    [request.employeeFirstName, request.employeeLastName].filter(Boolean).join(' ') ||
    `Employee #${request.employeeId}`

  async function handleDecide(approve: boolean) {
    setDecideError(null)
    try {
      await decide.mutateAsync({ id: request.id, body: { approve, reviewNote: note || undefined } })
    } catch (err) {
      setDecideError(toApiProblem(err, 'Could not record this decision.').message)
    }
  }

  return (
    <div className="space-y-3 rounded-md border p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <span className="font-medium">{employeeName}</span>
          <span className="ml-2 text-xs font-medium uppercase tracking-wide text-muted-foreground">
            {request.changeKind}
          </span>
        </div>
        <span className="text-xs text-muted-foreground">{formatInstant(request.createdAt)}</span>
      </div>

      <ChangeSummary request={request} positionNames={positionNames} />

      <p className="text-sm">
        <span className="text-muted-foreground">Reason: </span>
        {request.reason}
      </p>

      <div className="flex flex-wrap items-center gap-2">
        <input
          type="text"
          value={note}
          onChange={(e) => setNote(e.target.value)}
          placeholder="Review note (optional)"
          className="h-9 min-w-48 flex-1 rounded-md border border-input bg-background px-3 text-sm"
        />
        <Button size="sm" disabled={decide.isPending} onClick={() => void handleDecide(true)}>
          Approve
        </Button>
        <Button
          size="sm"
          variant="outline"
          disabled={decide.isPending}
          onClick={() => void handleDecide(false)}
        >
          Deny
        </Button>
      </div>
      {decideError && <p className="text-sm text-destructive">{decideError}</p>}
    </div>
  )
}
