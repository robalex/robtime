import { parseLocalDate, formatLocalDate } from '@/lib/dates'
import { cn } from '@/lib/utils'
import type { DifferentialOutcome, ShiftDifferentialExplanation } from './queries'

const OUTCOME_LABELS: Record<DifferentialOutcome, string> = {
  Applied: 'Applied',
  SupersededByExclusivityGroup: 'Lost exclusivity',
  BelowMinHoursInWindow: 'Below minimum hours',
  BelowMinHoursInRange: 'Below range minimum',
  NotActiveOnAnyWorkedDay: 'Not active that day',
  NoWindowOverlap: "Window didn't overlap",
  NotEnabledByPayRule: 'Not enabled by pay rule',
  ShiftHasMissingPunches: 'Incomplete shift',
}

const OUTCOME_STYLES: Record<DifferentialOutcome, string> = {
  Applied: 'bg-emerald-600/10 text-emerald-800 dark:text-emerald-400',
  SupersededByExclusivityGroup: 'bg-amber-600/10 text-amber-800 dark:text-amber-400',
  BelowMinHoursInWindow: 'bg-muted text-muted-foreground',
  BelowMinHoursInRange: 'bg-muted text-muted-foreground',
  NotActiveOnAnyWorkedDay: 'bg-muted text-muted-foreground',
  NoWindowOverlap: 'bg-muted text-muted-foreground',
  NotEnabledByPayRule: 'bg-destructive/10 text-destructive',
  ShiftHasMissingPunches: 'bg-destructive/10 text-destructive',
}

/**
 * The sandbox's actual payoff: the calendar shows *where* a differential could apply, this shows
 * *why* it did or didn't for the punches actually entered — every rule the pay rule enables gets a
 * row, even the ones that never came close, since "I created the rule but nothing happens" is the
 * single most likely reason someone opens this tool (see DifferentialOutcome.NotEnabledByPayRule).
 */
export function ExplanationPanel({ shifts }: { shifts: ShiftDifferentialExplanation[] }) {
  if (shifts.length === 0) {
    return (
      <p className="text-sm text-muted-foreground">
        Enter at least one In/Out pair of test punches above and run the sandbox to see per-rule verdicts.
      </p>
    )
  }

  return (
    <div className="space-y-3">
      {shifts.map((shift) => (
        <div key={`${shift.shiftDate}-${shift.anchorPunchId}`} className="rounded-md border p-3">
          <p className="text-sm font-medium">{formatLocalDate(parseLocalDate(shift.shiftDate))}</p>
          <ul className="mt-2 space-y-1.5">
            {shift.evaluations.map((evaluation) => (
              <li key={evaluation.code} className="flex flex-wrap items-baseline gap-x-2 gap-y-1 text-sm">
                <span
                  className={cn(
                    'shrink-0 rounded-full px-2 py-0.5 text-xs font-medium',
                    OUTCOME_STYLES[evaluation.outcome],
                  )}
                >
                  {OUTCOME_LABELS[evaluation.outcome]}
                </span>
                <span className="shrink-0 font-medium">{evaluation.code}</span>
                {evaluation.qualifyingHours > 0 && (
                  <span className="shrink-0 text-xs tabular-nums text-muted-foreground">
                    {evaluation.qualifyingHours.toFixed(2)}h / ${evaluation.amount.toFixed(2)}
                  </span>
                )}
                <span className="text-xs text-muted-foreground">{evaluation.explanation}</span>
              </li>
            ))}
          </ul>
        </div>
      ))}
    </div>
  )
}
