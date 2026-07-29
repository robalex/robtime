import { toApiProblem } from '@/lib/problem'
import type { ResolveLocalPunchTimeRequest } from './queries'

export type ResolveOutcome =
  | { kind: 'resolved'; instant: string }
  | { kind: 'ambiguous'; message: string }
  | { kind: 'error'; message: string }

/** `<input type="datetime-local">` gives "2026-06-01T09:00" — no seconds. NodaTime's LocalDateTime
 * wire format (LocalDateTimePattern.ExtendedIso) wants them present. */
function toLocalDateTimeWire(when: string): string {
  return when.length === 16 ? `${when}:00` : when
}

/**
 * Turns a `<input type="datetime-local">` string + IANA zone into a real Instant via
 * POST /punches/resolve-local-time — the same DST-aware resolution punch import applies to CSV rows
 * (LocalTimeResolver on the backend), so a punch entered by hand and one imported from a file that
 * name the same local wall-clock time land on the same UTC instant. `ambiguous` is a distinct outcome
 * from `error`: it means the local time is real but happens twice (the fall-back overlap) and the
 * caller should ask which occurrence via `daylightSaving`, not just report a plain failure.
 */
export async function resolveRowInstant(
  resolveLocalTime: (request: ResolveLocalPunchTimeRequest) => Promise<{ punchTime: string }>,
  when: string,
  timeZoneId: string,
  daylightSaving: boolean | undefined,
): Promise<ResolveOutcome> {
  try {
    const resolved = await resolveLocalTime({
      punchTime: toLocalDateTimeWire(when),
      punchTimeZoneId: timeZoneId,
      daylightSaving,
    })
    return { kind: 'resolved', instant: resolved.punchTime }
  } catch (err) {
    const problem = toApiProblem(err, 'Could not resolve this punch time.')
    if (problem.fieldErrors.DaylightSaving) {
      return { kind: 'ambiguous', message: problem.fieldErrors.DaylightSaving }
    }
    return {
      kind: 'error',
      message: problem.fieldErrors.PunchTime ?? problem.fieldErrors.PunchTimeZoneId ?? problem.message,
    }
  }
}
