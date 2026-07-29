import { useState } from 'react'
import { ChevronDown, ChevronLeft, ChevronRight, Lock, TriangleAlert } from 'lucide-react'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { formatLocalDate, parseLocalDate, toWireLocalDate } from '@/lib/dates'
import { toApiProblem } from '@/lib/problem'
import { useMe } from '@/auth/useMe'
import { can } from '@/lib/permissions'
import {
  useApproveTimecard,
  useTimecard,
  useUnapproveTimecard,
  type Timecard as TimecardData,
} from './queries'

type Workweek = TimecardData['workweeks'][number]
type Day = Workweek['days'][number]
type Shift = Day['shifts'][number]
type Pair = Shift['pairs'][number]

const HOURS_FORMAT = new Intl.NumberFormat('en-US', { minimumFractionDigits: 1, maximumFractionDigits: 1 })
const MONEY_FORMAT = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })

function formatHours(hours: number): string {
  return `${HOURS_FORMAT.format(hours)}h`
}

function formatMoney(amount: number): string {
  return MONEY_FORMAT.format(amount)
}

// Browser-local time, matching ClockCard.tsx's own formatTime — the common case (an employee on
// their own device, or a supervisor in the same facility) is correct with no extra plumbing, and
// TimecardResponse doesn't carry the employee's HomeTimeZoneId today. Cross-timezone supervisor
// review is a real gap this doesn't solve, not a case being silently claimed as handled.
function formatTime(iso: string): string {
  return new Date(iso).toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' })
}

function formatInstant(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  })
}

/**
 * Built once, mounted twice (UI_PLAN.md's `/time` first-person vs. People `?tab=punches` split) —
 * every prop this needs is `employeeId` and an optional `date` inside the target period; who's
 * looking and why is entirely the caller's concern, not this component's.
 */
export function Timecard({ employeeId }: { employeeId: number }) {
  const [date, setDate] = useState<string | undefined>(undefined)
  const { data: timecard, isPending, isError, error } = useTimecard(employeeId, date)
  const { data: me } = useMe()
  const approve = useApproveTimecard(employeeId, date)
  const unapprove = useUnapproveTimecard(employeeId, date)
  const [approvalError, setApprovalError] = useState<string | null>(null)

  if (isPending) {
    return <p className="text-sm text-muted-foreground">Loading timecard…</p>
  }

  if (isError) {
    return <p className="text-sm text-destructive">{toApiProblem(error, 'Could not load this timecard.').message}</p>
  }

  async function handleApprove() {
    setApprovalError(null)
    try {
      await approve.mutateAsync()
    } catch (err) {
      setApprovalError(toApiProblem(err, 'Could not approve this timecard.').message)
    }
  }

  async function handleUnapprove() {
    setApprovalError(null)
    try {
      await unapprove.mutateAsync()
    } catch (err) {
      setApprovalError(toApiProblem(err, 'Could not reopen this timecard.').message)
    }
  }

  const periodStart = parseLocalDate(timecard.periodStart)
  const periodEnd = parseLocalDate(timecard.periodEnd)
  const totals = timecard.workweeks.reduce(
    (acc, week) => ({
      regularHours: acc.regularHours + week.regularHours,
      overtimeHours: acc.overtimeHours + week.overtimeHours,
      doubletimeHours: acc.doubletimeHours + week.doubletimeHours,
    }),
    { regularHours: 0, overtimeHours: 0, doubletimeHours: 0 },
  )

  const incompletePairs = timecard.workweeks
    .flatMap((week) => week.days)
    .flatMap((day) => day.shifts.map((shift) => ({ day, shift })))
    .flatMap(({ day, shift }) => shift.pairs.filter((pair) => pair.isIncomplete).map((pair) => ({ day, pair })))

  return (
    <Card>
      <CardHeader className="flex-row flex-wrap items-center justify-between gap-4 space-y-0">
        <CardTitle className="text-base">Timecard</CardTitle>
        <div className="flex items-center gap-2">
          <Button
            variant="outline"
            size="icon"
            aria-label="Previous pay period"
            onClick={() => setDate(toWireLocalDate(periodStart.minusDays(1)))}
          >
            <ChevronLeft className="size-4" />
          </Button>
          <div className="text-center text-sm">
            <div>
              {formatLocalDate(periodStart)} – {formatLocalDate(periodEnd)}
            </div>
            <div className="text-xs text-muted-foreground">
              {timecard.payRuleName} pay rule
            </div>
          </div>
          <Button
            variant="outline"
            size="icon"
            aria-label="Next pay period"
            onClick={() => setDate(toWireLocalDate(periodEnd.plusDays(1)))}
          >
            <ChevronRight className="size-4" />
          </Button>
        </div>
      </CardHeader>

      <CardContent className="space-y-6">
        <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
          <Stat label="Regular" value={formatHours(totals.regularHours)} />
          <Stat label="Overtime" value={formatHours(totals.overtimeHours)} />
          <Stat label="Doubletime" value={formatHours(totals.doubletimeHours)} />
          <Stat label="Gross pay" value={formatMoney(timecard.grossPay)} emphasize />
        </div>

        {/* Locked status is worth showing to anyone who can see this timecard — an employee whose
            period is approved should know why an edit request would be refused. The action button
            itself stays Supervisor+ only, matching the API (UI_PLAN.md decision 21). */}
        {(timecard.isLocked || can.approveTimecard(me)) && (
          <div className="flex flex-wrap items-center justify-between gap-3 rounded-md border p-3">
            <div className="flex items-center gap-2 text-sm">
              {timecard.isLocked ? (
                <>
                  <Lock className="size-4 text-muted-foreground" />
                  <span>
                    Approved by {timecard.approvedByUserId}
                    {timecard.approvedAt ? ` on ${formatInstant(timecard.approvedAt)}` : ''}
                  </span>
                </>
              ) : (
                <span className="text-muted-foreground">Not yet approved.</span>
              )}
            </div>
            {can.approveTimecard(me) && (
              <Button
                size="sm"
                variant={timecard.isLocked ? 'outline' : 'default'}
                disabled={approve.isPending || unapprove.isPending}
                onClick={() => void (timecard.isLocked ? handleUnapprove() : handleApprove())}
              >
                {timecard.isLocked
                  ? unapprove.isPending
                    ? 'Reopening…'
                    : 'Unapprove'
                  : approve.isPending
                    ? 'Approving…'
                    : 'Approve'}
              </Button>
            )}
          </div>
        )}
        {approvalError && <p className="text-sm text-destructive">{approvalError}</p>}

        {incompletePairs.length > 0 && (
          <div className="flex items-start gap-3 rounded-md border border-amber-600/30 bg-amber-600/10 p-3">
            <TriangleAlert className="mt-0.5 size-4 shrink-0 text-amber-700 dark:text-amber-500" />
            <div className="text-sm">
              <p className="font-medium text-amber-800 dark:text-amber-400">
                {incompletePairs.length === 1 ? '1 punch needs attention' : `${incompletePairs.length} punches need attention`}
              </p>
              <p className="text-muted-foreground">
                {incompletePairs
                  .map(({ day, pair }) => `${formatLocalDate(parseLocalDate(day.date))} has a clock-${pair.in ? 'in' : 'out'} with no matching clock-${pair.in ? 'out' : 'in'}`)
                  .join('. ')}
                .
              </p>
            </div>
          </div>
        )}

        <div className="space-y-4">
          {timecard.workweeks.map((week) => (
            <WorkweekSection key={week.weekStart} week={week} />
          ))}
        </div>
      </CardContent>
    </Card>
  )
}

function Stat({ label, value, emphasize }: { label: string; value: string; emphasize?: boolean }) {
  return (
    <div>
      <p className="text-xs uppercase tracking-wide text-muted-foreground">{label}</p>
      <p className={cn('text-xl tabular-nums', emphasize ? 'font-bold' : 'font-medium')}>{value}</p>
    </div>
  )
}

function WorkweekSection({ week }: { week: Workweek }) {
  const [open, setOpen] = useState(true)
  // Not "does this week have paid hours" — an all-incomplete week (orphan punches only) has zero
  // hours by construction (PunchPair.TotalHours is 0 for a missing pair) but still has real shift
  // data the exceptions callout is pointing at. Gating on hours would silently hide exactly the
  // punches a supervisor most needs to see.
  const hasAnyShifts = week.days.some((day) => day.shifts.length > 0)

  return (
    <div className="rounded-md border">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        className="flex w-full items-center justify-between gap-4 px-4 py-3 text-left hover:bg-accent"
      >
        <span className="text-sm font-medium">Week of {formatLocalDate(parseLocalDate(week.weekStart))}</span>
        <span className="flex items-center gap-3 text-sm text-muted-foreground">
          {week.regularRate != null && <span>Regular rate {formatMoney(week.regularRate)}/hr</span>}
          <span className="tabular-nums text-foreground">{formatMoney(week.gross)}</span>
          <ChevronDown className={cn('size-4 transition-transform', open ? '' : '-rotate-90')} />
        </span>
      </button>

      {open && (
        <div className="space-y-1 border-t px-4 py-2">
          {hasAnyShifts ? (
            week.days.map((day) => <DayRow key={day.date} day={day} />)
          ) : (
            <p className="py-2 text-sm text-muted-foreground">No punches this week.</p>
          )}
        </div>
      )}
    </div>
  )
}

function DayRow({ day }: { day: Day }) {
  if (day.shifts.length === 0) {
    return (
      <div className="flex items-center justify-between border-t py-2 text-sm text-muted-foreground first:border-t-0">
        <span>{formatLocalDate(parseLocalDate(day.date))}</span>
        <span>—</span>
      </div>
    )
  }

  return (
    <div className="space-y-2 border-t py-2 first:border-t-0">
      <p className="text-sm text-muted-foreground">{formatLocalDate(parseLocalDate(day.date))}</p>
      {day.shifts.map((shift) => (
        <ShiftDetail key={shift.anchorPunchId} shift={shift} />
      ))}
    </div>
  )
}

function ShiftDetail({ shift }: { shift: Shift }) {
  return (
    <div className="space-y-1 pl-3">
      {shift.pairs.map((pair, i) => (
        <PairRow key={i} pair={pair} />
      ))}
      {shift.fixedEntries.map((entry) => (
        <div key={entry.id} className="flex items-center justify-between text-sm">
          <span>{entry.kind === 'FixedDollar' ? 'Bonus' : 'Paid hours'}</span>
          <span className="tabular-nums">
            {entry.kind === 'FixedDollar' ? formatMoney(entry.amount ?? 0) : formatHours(entry.hours ?? 0)}
          </span>
        </div>
      ))}
      {shift.lineItems.length > 0 && (
        <div className="space-y-0.5 border-l pl-3 text-xs text-muted-foreground">
          {shift.lineItems.map((item, i) => (
            <div key={i} className="flex justify-between">
              <span>
                {item.description}
                {item.hours ? ` · ${formatHours(item.hours)}` : ''}
                {item.baseRate != null ? ` @ ${formatMoney(item.baseRate)}` : ''}
              </span>
              <span className="tabular-nums">{formatMoney(item.amount)}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

function PairRow({ pair }: { pair: Pair }) {
  return (
    <div className={cn('flex items-center justify-between text-sm', pair.isIncomplete && 'text-amber-700 dark:text-amber-500')}>
      <span className="flex items-center gap-2">
        <span>
          {pair.in ? formatTime(pair.in.time) : <em className="not-italic text-muted-foreground">missing</em>}
          {' – '}
          {pair.out ? formatTime(pair.out.time) : <em className="not-italic">missing</em>}
        </span>
        {pair.positionName && <span className="text-xs text-muted-foreground">{pair.positionName}</span>}
        {pair.isSplit && <span className="text-xs text-muted-foreground">(split)</span>}
      </span>
      <span className="tabular-nums">{formatHours(pair.hours)}</span>
    </div>
  )
}
