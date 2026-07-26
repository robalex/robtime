import { ChronoUnit, LocalDate } from '@js-joda/core'
import type { ReactNode } from 'react'
import { formatLocalDate, todayLocalDate } from '@/lib/dates'
import { cn } from '@/lib/utils'

/**
 * The one effective-dated widget (UI_PLAN.md §6 Rule 4) — a horizontal band of periods plus a
 * "Change effective…" action. `EmployeePositionAssignment` and `PayRuleAssignment` are the same
 * `(thing, from, to?)` shape, so this component knows nothing about positions or pay rules: it
 * takes a `label`/`sublabel` per period and lets the caller supply the actual navigation (own
 * route, never a modal — see the corollary in §6).
 */
export interface TimelinePeriod {
  id: number | string
  from: LocalDate
  /** Null means still in effect. */
  to: LocalDate | null
  label: string
  sublabel?: string
}

interface EffectiveDatedTimelineProps {
  periods: TimelinePeriod[]
  emptyMessage: string
  /** Rendered top-right — typically a `<Link>` to the "new assignment" route. */
  changeEffectiveAction: ReactNode
  /** Rendered per period in the list below the band — typically a "Change" `<Link>`. */
  renderPeriodAction?: (period: TimelinePeriod) => ReactNode
}

function daysBetween(a: LocalDate, b: LocalDate): number {
  return a.until(b, ChronoUnit.DAYS)
}

export function EffectiveDatedTimeline({
  periods,
  emptyMessage,
  changeEffectiveAction,
  renderPeriodAction,
}: EffectiveDatedTimelineProps) {
  const sorted = [...periods].sort((a, b) => a.from.compareTo(b.from))

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-end">{changeEffectiveAction}</div>

      {sorted.length === 0 ? (
        <p className="text-sm text-muted-foreground">{emptyMessage}</p>
      ) : (
        <>
          <TimelineBand periods={sorted} />
          <ul className="divide-y rounded-lg border">
            {[...sorted].reverse().map((period) => (
              <li key={period.id} className="flex items-center justify-between gap-4 px-4 py-3">
                <div>
                  <p className="text-sm font-medium">{period.label}</p>
                  {period.sublabel && <p className="text-sm text-muted-foreground">{period.sublabel}</p>}
                  <p className="text-xs text-muted-foreground">
                    {formatLocalDate(period.from)} – {period.to ? formatLocalDate(period.to) : 'present'}
                  </p>
                </div>
                {renderPeriodAction?.(period)}
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  )
}

// Periods are assumed pre-sorted by `from` ascending and non-overlapping — the API enforces the
// latter (PositionAssignmentValidator.FindConflict), so a gap in the band reflects a real gap in
// coverage, not a rendering artifact.
function TimelineBand({ periods }: { periods: TimelinePeriod[] }) {
  const today = todayLocalDate()
  const start = periods[0].from
  // An open period (to === null) is "in effect until further notice" — normally that means
  // today, but a future-dated period (from > today, e.g. "starts next Monday") is still open-ended
  // and its own start is later than today, so the band has to reach at least that far or the
  // period would render with a negative/zero span.
  const end = periods.reduce((latest, period) => {
    const periodEnd = period.to ?? (period.from.isAfter(today) ? period.from : today)
    return periodEnd.isAfter(latest) ? periodEnd : latest
  }, today)
  const totalDays = Math.max(1, daysBetween(start, end))

  type Segment =
    | { kind: 'gap'; key: string; from: LocalDate; to: LocalDate }
    | { kind: 'period'; key: string; from: LocalDate; to: LocalDate; period: TimelinePeriod }

  const segments: Segment[] = []
  let cursor = start
  for (const period of periods) {
    if (period.from.isAfter(cursor)) {
      segments.push({ kind: 'gap', key: `gap-${cursor.toString()}`, from: cursor, to: period.from })
    }
    const periodEnd = period.to ?? end
    segments.push({ kind: 'period', key: String(period.id), from: period.from, to: periodEnd, period })
    if (periodEnd.isAfter(cursor)) {
      cursor = periodEnd
    }
  }
  if (cursor.isBefore(end)) {
    segments.push({ kind: 'gap', key: `gap-${cursor.toString()}-end`, from: cursor, to: end })
  }

  // today's marker only means something strictly inside the band — at either edge it would just
  // sit on top of the band's own border.
  const todayOffsetPercent =
    today.isAfter(start) && today.isBefore(end) ? (daysBetween(start, today) / totalDays) * 100 : null

  return (
    <div className="space-y-1">
      <div className="relative">
        <div className="flex h-8 w-full overflow-hidden rounded-md border">
          {segments.map((segment) => {
            const width = Math.max(2, (daysBetween(segment.from, segment.to) / totalDays) * 100)
            const isOngoing = segment.kind === 'period' && segment.period.to === null
            return (
              <div
                key={segment.key}
                title={
                  segment.kind === 'gap'
                    ? `No position assigned: ${formatLocalDate(segment.from)} – ${formatLocalDate(segment.to)}`
                    : `${segment.period.label}: ${formatLocalDate(segment.from)} – ${segment.period.to ? formatLocalDate(segment.to) : 'present'}`
                }
                className={cn(
                  'h-full border-r last:border-r-0',
                  segment.kind === 'gap' && 'bg-muted',
                  segment.kind === 'period' && (isOngoing ? 'bg-primary/80' : 'bg-primary/35'),
                )}
                style={
                  segment.kind === 'gap'
                    ? {
                        width: `${width}%`,
                        backgroundImage:
                          'repeating-linear-gradient(45deg, var(--border), var(--border) 4px, transparent 4px, transparent 8px)',
                      }
                    : { width: `${width}%` }
                }
              />
            )
          })}
        </div>
        {todayOffsetPercent !== null && (
          <div
            className="absolute top-0 h-8 w-px bg-foreground"
            style={{ left: `${todayOffsetPercent}%` }}
            title={`Today — ${formatLocalDate(today)}`}
          />
        )}
      </div>
      <div className="flex justify-between text-xs text-muted-foreground">
        <span>{formatLocalDate(start)}</span>
        <span>{formatLocalDate(end)}{end.isEqual(today) && ' (today)'}</span>
      </div>
    </div>
  )
}
