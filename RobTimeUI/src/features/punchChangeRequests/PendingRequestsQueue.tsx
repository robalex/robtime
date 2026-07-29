import { useState } from 'react'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { useMe } from '@/auth/useMe'
import { usePositions } from '@/features/positions/queries'
import { toApiProblem } from '@/lib/problem'
import {
  useDecidePunchChangeRequest,
  usePendingPunchChangeRequests,
  type PunchChangeRequest,
} from './queries'

type Punch = NonNullable<PunchChangeRequest['currentPunch']>

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

function ChangeSummary({
  request,
  positionNames,
}: {
  request: PunchChangeRequest
  positionNames: Map<number, string>
}) {
  if (request.changeKind === 'Add') {
    const positionLabel = positionLabelFor(request.requestedPositionId, positionNames)
    return (
      <p className="text-sm">
        New {request.requestedKind ?? '—'} punch
        {request.requestedPunchTime ? ` · ${formatInstant(request.requestedPunchTime)}` : ''}
        {positionLabel ? ` · ${positionLabel}` : ''}
      </p>
    )
  }

  if (request.changeKind === 'Delete') {
    return (
      <p className="text-sm">
        <span className="text-muted-foreground">Delete: </span>
        {request.currentPunch ? describePunch(request.currentPunch) : 'this punch (no longer exists)'}
      </p>
    )
  }

  // Edit requests are partial patches — only the fields actually being changed carry a Requested*
  // value (UI_PLAN.md's own note on this table: "null = leave the existing punch's [field] alone").
  // List only those, current-vs-requested, rather than a full-field dump that would misrepresent
  // untouched fields as part of the change.
  const fields = changedFields(request, positionNames)

  if (!request.currentPunch) {
    return <p className="text-sm text-destructive">The target punch no longer exists.</p>
  }

  if (fields.length === 0) {
    return <p className="text-sm text-muted-foreground">No field changes recorded.</p>
  }

  return (
    <div className="space-y-0.5 text-sm">
      {fields.map((field) => (
        <p key={field.label}>
          <span className="text-muted-foreground">{field.label}: </span>
          {field.current} → {field.requested}
        </p>
      ))}
    </div>
  )
}

function changedFields(
  request: PunchChangeRequest,
  positionNames: Map<number, string>,
): { label: string; current: string; requested: string }[] {
  const current = request.currentPunch
  if (!current) {
    return []
  }

  const fields: { label: string; current: string; requested: string }[] = []

  if (request.requestedPunchTime) {
    fields.push({ label: 'Time', current: formatInstant(current.punchTime), requested: formatInstant(request.requestedPunchTime) })
  }
  if (request.requestedKind) {
    fields.push({ label: 'Kind', current: current.kind, requested: request.requestedKind })
  }
  if (request.requestedSubtype) {
    fields.push({ label: 'Subtype', current: current.subtype ?? 'None', requested: request.requestedSubtype })
  }
  if (request.requestedPositionId != null) {
    fields.push({
      label: 'Position',
      current: current.positionId != null ? positionLabelFor(current.positionId, positionNames)! : '—',
      requested: positionLabelFor(request.requestedPositionId, positionNames)!,
    })
  }
  if (request.requestedAmount != null) {
    fields.push({ label: 'Amount', current: formatMoney(current.amount), requested: formatMoney(request.requestedAmount) })
  }
  if (request.requestedHours != null) {
    fields.push({ label: 'Hours', current: current.hours?.toString() ?? '—', requested: request.requestedHours.toString() })
  }
  if (request.requestedBonusKind) {
    fields.push({ label: 'Bonus kind', current: current.bonusKind ?? '—', requested: request.requestedBonusKind })
  }
  if (request.requestedCountsTowardRegularRate != null) {
    fields.push({
      label: 'Counts toward regular rate',
      current: current.countsTowardRegularRate ? 'Yes' : 'No',
      requested: request.requestedCountsTowardRegularRate ? 'Yes' : 'No',
    })
  }

  return fields
}

function describePunch(punch: Punch): string {
  return `${punch.kind} · ${formatInstant(punch.punchTime)}`
}

function positionLabelFor(id: number | null | undefined, names: Map<number, string>): string | null {
  if (id == null) {
    return null
  }
  return names.get(id) ?? `Position #${id}`
}

function formatMoney(amount: number | null | undefined): string {
  return amount != null ? `$${amount.toFixed(2)}` : '—'
}

// Browser-local time, matching Timecard.tsx's own formatTime for the same reason (no employee
// timezone carried on this response either) — date included here since a request can be days old.
function formatInstant(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  })
}
