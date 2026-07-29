import type { PunchChangeRequest } from './queries'

type Punch = NonNullable<PunchChangeRequest['currentPunch']>

export function describePunch(punch: Punch): string {
  return `${punch.kind} · ${formatInstant(punch.punchTime)}`
}

export function positionLabelFor(id: number | null | undefined, names: Map<number, string>): string | null {
  if (id == null) {
    return null
  }
  return names.get(id) ?? `Position #${id}`
}

export function formatMoney(amount: number | null | undefined): string {
  return amount != null ? `$${amount.toFixed(2)}` : '—'
}

// Browser-local time, matching Timecard.tsx's own formatTime for the same reason (no employee
// timezone carried on this response either) — date included here since a request can be days old.
export function formatInstant(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  })
}
