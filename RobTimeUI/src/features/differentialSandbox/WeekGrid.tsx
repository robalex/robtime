import { LocalDate } from '@js-joda/core'
import { formatLocalDate } from '@/lib/dates'
import { cn } from '@/lib/utils'
import { toZonedParts } from './localTime'
import type { DifferentialOutcome, DifferentialZone } from './queries'

const GRID_HEIGHT_PX = 720 // 30px/hour
const SECONDS_PER_DAY = 86400

// Outcomes with segments worth drawing at all — the rest (NotActiveOnAnyWorkedDay, NoWindowOverlap,
// NotEnabledByPayRule, ShiftHasMissingPunches) never carry any Segments to draw in the first place.
const WON_OUTCOMES: ReadonlySet<DifferentialOutcome> = new Set(['Applied'])

interface DaySegment {
  code: string
  startSec: number
  endSec: number
  continuesBefore: boolean
  continuesAfter: boolean
  fullStartLabel: string
  fullEndLabel: string
  lane: number
}

export interface EvaluationSegmentInput {
  code: string
  outcome: DifferentialOutcome
  start: string
  end: string
}

interface EvaluationDaySegment {
  code: string
  outcome: DifferentialOutcome
  startSec: number
  endSec: number
}

interface WeekGridProps {
  windowStart: LocalDate
  dayCount: number
  timeZone: string
  zones: DifferentialZone[]
  colorByCode: Map<string, string>
  /** Qualifying segments from test-punch evaluations, overlaid on top of the zones they fall
   * within. Optional — Slice 1 callers with no test punches simply omit it. */
  evaluationSegments?: EvaluationSegmentInput[]
}

/**
 * Hand-rolled calendar grid — no calendar library, matching EffectiveDatedTimeline's own precedent
 * for this kind of hand-rolled proportional band. Each zone's Instant boundaries are converted into
 * the employee's own zone via toZonedParts purely for *positioning*; the zone's existence and exact
 * timing were already fully decided server-side. A zone spanning multiple days is split into one
 * segment per visible day it touches, with a truncation marker on whichever edge continues past the
 * visible window (or past that calendar day, for a multi-day range zone).
 */
export function WeekGrid({ windowStart, dayCount, timeZone, zones, colorByCode, evaluationSegments }: WeekGridProps) {
  const days = Array.from({ length: dayCount }, (_, i) => windowStart.plusDays(i))

  return (
    <div className="overflow-x-auto rounded-lg border">
      <div className="flex min-w-[720px]">
        <HourGutter />
        {days.map((day) => (
          <DayColumn
            key={day.toString()}
            day={day}
            timeZone={timeZone}
            zones={zones}
            colorByCode={colorByCode}
            evaluationSegments={evaluationSegments ?? []}
          />
        ))}
      </div>
    </div>
  )
}

function HourGutter() {
  const hours = [0, 3, 6, 9, 12, 15, 18, 21, 24]
  return (
    <div className="w-12 shrink-0 border-r bg-muted/30">
      <div className="h-10 border-b" />
      <div className="relative" style={{ height: GRID_HEIGHT_PX }}>
        {hours.map((h) => (
          <span
            key={h}
            className="absolute right-1 -translate-y-1/2 text-[10px] text-muted-foreground tabular-nums"
            style={{ top: `${(h / 24) * 100}%` }}
          >
            {h}
          </span>
        ))}
      </div>
    </div>
  )
}

function DayColumn({
  day,
  timeZone,
  zones,
  colorByCode,
  evaluationSegments,
}: {
  day: LocalDate
  timeZone: string
  zones: DifferentialZone[]
  colorByCode: Map<string, string>
  evaluationSegments: EvaluationSegmentInput[]
}) {
  const segments = laneAssign(segmentsForDay(day, timeZone, zones))
  const laneCount = Math.max(1, ...segments.map((s) => s.lane + 1))
  const evalSegments = evaluationSegmentsForDay(day, timeZone, evaluationSegments)

  return (
    <div className="w-40 shrink-0 border-r last:border-r-0">
      <div className="flex h-10 items-center justify-center border-b text-xs font-medium">
        {formatLocalDate(day)}
      </div>
      <div
        className="relative"
        style={{
          height: GRID_HEIGHT_PX,
          backgroundImage:
            'repeating-linear-gradient(to bottom, var(--border) 0, var(--border) 1px, transparent 1px, transparent calc(100%/24))',
        }}
      >
        {segments.map((segment, index) => {
          const topPercent = (segment.startSec / SECONDS_PER_DAY) * 100
          const heightPercent = Math.max(1, ((segment.endSec - segment.startSec) / SECONDS_PER_DAY) * 100)
          return (
            <div
              key={index}
              title={`${segment.code}: ${segment.fullStartLabel} – ${segment.fullEndLabel}`}
              className={cn(
                'absolute overflow-hidden border-l-2 px-1 py-0.5 text-[10px] font-medium text-white',
                colorByCode.get(segment.code) ?? 'bg-slate-500/70 border-slate-600',
                !segment.continuesBefore && 'rounded-t-sm',
                !segment.continuesAfter && 'rounded-b-sm',
              )}
              style={{
                top: `${topPercent}%`,
                height: `${heightPercent}%`,
                left: `${(segment.lane / laneCount) * 100}%`,
                width: `${100 / laneCount}%`,
              }}
            >
              {segment.continuesBefore && '▲ '}
              {segment.code}
              {segment.continuesAfter && ' ▼'}
            </div>
          )
        })}

        {/* Evaluation overlay: where a test punch actually qualified. Inset and layered above the
            zones so a winner (solid, ring-highlighted) and a loser (hatched) both stay visible even
            when they cover the exact same window — that overlap *is* the exclusivity conflict. */}
        {evalSegments.map((segment, index) => {
          const topPercent = (segment.startSec / SECONDS_PER_DAY) * 100
          const heightPercent = Math.max(1, ((segment.endSec - segment.startSec) / SECONDS_PER_DAY) * 100)
          const won = WON_OUTCOMES.has(segment.outcome)
          return (
            <div
              key={`eval-${index}`}
              title={`${segment.code}: ${won ? 'applied' : 'did not apply'} here`}
              className={cn(
                'absolute rounded-sm border-2',
                won ? cn(colorByCode.get(segment.code)?.split(' ')[0], 'border-white ring-1 ring-black/20 dark:ring-white/30') : 'border-destructive/60',
              )}
              style={{
                top: `${topPercent}%`,
                height: `${heightPercent}%`,
                left: '30%',
                width: '40%',
                ...(won
                  ? {}
                  : {
                      backgroundImage:
                        'repeating-linear-gradient(45deg, var(--destructive) 0, var(--destructive) 2px, transparent 2px, transparent 6px)',
                      opacity: 0.5,
                    }),
              }}
            />
          )
        })}
      </div>
    </div>
  )
}

// Same day-touching logic as segmentsForDay, simplified: no lane assignment, since an evaluation
// segment overlapping its zone (or another rule's losing segment) is meaningful signal — the whole
// point of drawing it — not visual clutter to resolve.
function evaluationSegmentsForDay(
  day: LocalDate, timeZone: string, segments: EvaluationSegmentInput[],
): EvaluationDaySegment[] {
  const results: EvaluationDaySegment[] = []
  for (const segment of segments) {
    const start = toZonedParts(segment.start, timeZone)
    const end = toZonedParts(segment.end, timeZone)
    if (day.isBefore(start.date) || day.isAfter(end.date)) {
      continue
    }

    const startSec = start.date.isBefore(day) ? 0 : start.secondsOfDay
    const endSec = end.date.isAfter(day) ? SECONDS_PER_DAY : end.secondsOfDay
    if (endSec <= startSec) {
      continue
    }

    results.push({ code: segment.code, outcome: segment.outcome, startSec, endSec })
  }
  return results
}

// A zone touches `day` if its local start or end date falls on `day`, or if it spans straight
// through it (started on an earlier day, ends on a later one). continuesBefore/After mark whichever
// edge sits outside this specific calendar day, so the block reads as truncated rather than as the
// zone's actual full extent.
function segmentsForDay(day: LocalDate, timeZone: string, zones: DifferentialZone[]): DaySegment[] {
  const segments: DaySegment[] = []
  for (const zone of zones) {
    const start = toZonedParts(zone.start, timeZone)
    const end = toZonedParts(zone.end, timeZone)
    if (day.isBefore(start.date) || day.isAfter(end.date)) {
      continue
    }

    const continuesBefore = start.date.isBefore(day)
    const continuesAfter = end.date.isAfter(day)
    const startSec = continuesBefore ? 0 : start.secondsOfDay
    const endSec = continuesAfter ? SECONDS_PER_DAY : end.secondsOfDay
    if (endSec <= startSec) {
      continue
    }

    segments.push({
      code: zone.code,
      startSec,
      endSec,
      continuesBefore,
      continuesAfter,
      fullStartLabel: formatInstantLabel(zone.start, timeZone),
      fullEndLabel: formatInstantLabel(zone.end, timeZone),
      lane: 0,
    })
  }
  return segments
}

// Greedy interval-lane assignment (sort by start, place in the first lane whose last occupant has
// already ended) — the same technique a day-planner calendar uses to show overlapping appointments
// side by side rather than stacked on top of each other. Overlaps are the normal case here, not an
// edge case: two differentials genuinely active at once is exactly what this tool exists to surface.
function laneAssign(segments: DaySegment[]): DaySegment[] {
  const sorted = [...segments].sort((a, b) => a.startSec - b.startSec)
  const laneEnds: number[] = []
  for (const segment of sorted) {
    let lane = laneEnds.findIndex((end) => end <= segment.startSec)
    if (lane === -1) {
      lane = laneEnds.length
      laneEnds.push(segment.endSec)
    } else {
      laneEnds[lane] = segment.endSec
    }
    segment.lane = lane
  }
  return sorted
}

function formatInstantLabel(instantIso: string, timeZone: string): string {
  return new Date(instantIso).toLocaleString('en-US', {
    timeZone,
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  })
}
