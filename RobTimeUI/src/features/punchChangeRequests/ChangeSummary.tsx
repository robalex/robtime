import { describePunch, formatInstant, formatMoney, positionLabelFor } from './formatting'
import type { PunchChangeRequest } from './queries'

/**
 * Renders what a punch change request actually asks for — shared by PendingRequestsQueue (a
 * supervisor deciding it) and MyPunchRequests (the employee who submitted it, checking on it).
 * Split out of PendingRequestsQueue rather than duplicated, since both need the exact same
 * Add/Edit/Delete rendering logic and field-diffing.
 */
export function ChangeSummary({
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
