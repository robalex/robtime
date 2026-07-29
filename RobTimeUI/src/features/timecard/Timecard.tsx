import { useState } from 'react'
import { ChevronDown, ChevronLeft, ChevronRight, Lock, Pencil, Plus, TriangleAlert, X } from 'lucide-react'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select } from '@/components/ui/select'
import { cn } from '@/lib/utils'
import { formatLocalDate, parseLocalDate, toWireLocalDate } from '@/lib/dates'
import { toApiProblem } from '@/lib/problem'
import { useMe } from '@/auth/useMe'
import { can } from '@/lib/permissions'
import { useMyPunchChangeRequests, useSubmitPunchChangeRequest } from '@/features/punchChangeRequests/queries'
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
type PunchStub = NonNullable<Pair['in']>

/** What every row below needs to offer punch-change-request affordances — bundled into one prop
 * rather than three, since they always travel together down WorkweekSection -> DayRow ->
 * ShiftDetail -> PairRow. Undefined (not just canRequest: false) would work too, but a concrete
 * "off" value keeps every level's prop type the same shape rather than needing an extra branch. */
interface RequestScope {
  employeeId: number
  canRequestChanges: boolean
  pendingPunchIds: Set<number>
}

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

function toDateTimeLocal(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
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

  // Only the employee viewing their own, still-open period gets request affordances — a supervisor
  // reviewing someone else's timecard already has direct edit access (People -> Punches' fast entry
  // grid, PUT /punches), so routing them through a request-to-themselves would just be a detour.
  // `timecard?.isLocked ?? true` defaults to "can't request" before the timecard has loaded, since
  // useMyPunchChangeRequests below (like every hook) has to be called unconditionally regardless of
  // where the isPending/isError guards below end up returning early.
  const canRequestChanges = me?.employeeId === employeeId && !(timecard?.isLocked ?? true)
  const myRequests = useMyPunchChangeRequests(employeeId, canRequestChanges)
  const pendingPunchIds = new Set(
    (myRequests.data?.items ?? [])
      .filter((r) => r.status === 'Pending' && r.punchId != null)
      .map((r) => r.punchId!),
  )
  const requestScope: RequestScope = { employeeId, canRequestChanges, pendingPunchIds }

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
            <WorkweekSection key={week.weekStart} week={week} requestScope={requestScope} />
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

function WorkweekSection({ week, requestScope }: { week: Workweek; requestScope: RequestScope }) {
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
          {hasAnyShifts || requestScope.canRequestChanges ? (
            week.days.map((day) => <DayRow key={day.date} day={day} requestScope={requestScope} />)
          ) : (
            <p className="py-2 text-sm text-muted-foreground">No punches this week.</p>
          )}
        </div>
      )}
    </div>
  )
}

function DayRow({ day, requestScope }: { day: Day; requestScope: RequestScope }) {
  const [adding, setAdding] = useState(false)
  const { canRequestChanges } = requestScope

  const header = (
    <div className="flex items-center justify-between text-sm text-muted-foreground">
      <span>{formatLocalDate(parseLocalDate(day.date))}</span>
      {canRequestChanges ? (
        <Button variant="ghost" size="sm" className="h-6 px-2 text-xs" onClick={() => setAdding((o) => !o)}>
          <Plus className="size-3" /> Request punch
        </Button>
      ) : day.shifts.length === 0 ? (
        <span>—</span>
      ) : null}
    </div>
  )

  if (day.shifts.length === 0) {
    return (
      <div className="space-y-2 border-t py-2 first:border-t-0">
        {header}
        {adding && (
          <AddPunchForm employeeId={requestScope.employeeId} date={day.date} onDone={() => setAdding(false)} />
        )}
      </div>
    )
  }

  return (
    <div className="space-y-2 border-t py-2 first:border-t-0">
      {header}
      {adding && (
        <AddPunchForm employeeId={requestScope.employeeId} date={day.date} onDone={() => setAdding(false)} />
      )}
      {day.shifts.map((shift) => (
        <ShiftDetail key={shift.anchorPunchId} shift={shift} requestScope={requestScope} />
      ))}
    </div>
  )
}

function ShiftDetail({ shift, requestScope }: { shift: Shift; requestScope: RequestScope }) {
  return (
    <div className="space-y-1 pl-3">
      {shift.pairs.map((pair, i) => (
        <PairRow key={i} pair={pair} requestScope={requestScope} />
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

function PairRow({ pair, requestScope }: { pair: Pair; requestScope: RequestScope }) {
  const { employeeId, canRequestChanges, pendingPunchIds } = requestScope
  const [active, setActive] = useState<{ punch: PunchStub; mode: 'edit' | 'delete' } | null>(null)

  function toggle(punch: PunchStub, mode: 'edit' | 'delete') {
    setActive((current) => (current?.punch.id === punch.id && current.mode === mode ? null : { punch, mode }))
  }

  const punches = [pair.in, pair.out].filter((p): p is PunchStub => p != null)

  return (
    <div className="space-y-1">
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
        <span className="flex items-center gap-2">
          <span className="tabular-nums">{formatHours(pair.hours)}</span>
          {canRequestChanges &&
            punches.map((punch) =>
              pendingPunchIds.has(punch.id) ? (
                <span key={punch.id} className="text-xs text-muted-foreground">
                  pending
                </span>
              ) : (
                <span key={punch.id} className="flex items-center gap-1">
                  <button
                    type="button"
                    aria-label={`Request edit for ${formatTime(punch.time)}`}
                    onClick={() => toggle(punch, 'edit')}
                    className="text-muted-foreground hover:text-foreground"
                  >
                    <Pencil className="size-3.5" />
                  </button>
                  <button
                    type="button"
                    aria-label={`Request removal for ${formatTime(punch.time)}`}
                    onClick={() => toggle(punch, 'delete')}
                    className="text-muted-foreground hover:text-destructive"
                  >
                    <X className="size-3.5" />
                  </button>
                </span>
              ),
            )}
        </span>
      </div>
      {active?.mode === 'edit' && (
        <EditPunchForm employeeId={employeeId} punch={active.punch} onDone={() => setActive(null)} />
      )}
      {active?.mode === 'delete' && (
        <DeletePunchForm employeeId={employeeId} punch={active.punch} onDone={() => setActive(null)} />
      )}
    </div>
  )
}

function EditPunchForm({
  employeeId,
  punch,
  onDone,
}: {
  employeeId: number
  punch: PunchStub
  onDone: () => void
}) {
  const submit = useSubmitPunchChangeRequest(employeeId)
  const [when, setWhen] = useState(() => toDateTimeLocal(new Date(punch.time)))
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)

  async function handleSubmit() {
    setError(null)
    if (!reason.trim()) {
      setError('A reason is required.')
      return
    }
    try {
      await submit.mutateAsync({
        changeKind: 'Edit',
        punchId: punch.id,
        reason,
        punchTime: new Date(when).toISOString(),
      })
      onDone()
    } catch (err) {
      setError(toApiProblem(err, 'Could not submit this request.').message)
    }
  }

  return (
    <div className="flex flex-wrap items-end gap-2 rounded-md border bg-muted/30 p-2">
      <div className="space-y-1">
        <Label htmlFor={`edit-time-${punch.id}`} className="text-xs">
          New time
        </Label>
        <Input
          id={`edit-time-${punch.id}`}
          type="datetime-local"
          className="h-8"
          value={when}
          onChange={(e) => setWhen(e.target.value)}
        />
      </div>
      <div className="flex-1 space-y-1">
        <Label htmlFor={`edit-reason-${punch.id}`} className="text-xs">
          Reason
        </Label>
        <Input
          id={`edit-reason-${punch.id}`}
          className="h-8"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder="Why this change?"
        />
      </div>
      <Button size="sm" disabled={submit.isPending} onClick={() => void handleSubmit()}>
        {submit.isPending ? 'Submitting…' : 'Submit'}
      </Button>
      <Button size="sm" variant="outline" onClick={onDone}>
        Cancel
      </Button>
      {error && <p className="w-full text-xs text-destructive">{error}</p>}
    </div>
  )
}

function DeletePunchForm({
  employeeId,
  punch,
  onDone,
}: {
  employeeId: number
  punch: PunchStub
  onDone: () => void
}) {
  const submit = useSubmitPunchChangeRequest(employeeId)
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)

  async function handleSubmit() {
    setError(null)
    if (!reason.trim()) {
      setError('A reason is required.')
      return
    }
    try {
      await submit.mutateAsync({ changeKind: 'Delete', punchId: punch.id, reason })
      onDone()
    } catch (err) {
      setError(toApiProblem(err, 'Could not submit this request.').message)
    }
  }

  return (
    <div className="flex flex-wrap items-end gap-2 rounded-md border border-destructive/30 bg-destructive/5 p-2">
      <div className="flex-1 space-y-1">
        <Label htmlFor={`delete-reason-${punch.id}`} className="text-xs">
          Reason for removing this punch
        </Label>
        <Input
          id={`delete-reason-${punch.id}`}
          className="h-8"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder="Why should this be removed?"
        />
      </div>
      <Button size="sm" variant="destructive" disabled={submit.isPending} onClick={() => void handleSubmit()}>
        {submit.isPending ? 'Submitting…' : 'Request removal'}
      </Button>
      <Button size="sm" variant="outline" onClick={onDone}>
        Cancel
      </Button>
      {error && <p className="w-full text-xs text-destructive">{error}</p>}
    </div>
  )
}

type AddPunchKind = 'In' | 'Out' | 'FixedDollar' | 'FixedHours'

function AddPunchForm({ employeeId, date, onDone }: { employeeId: number; date: string; onDone: () => void }) {
  const submit = useSubmitPunchChangeRequest(employeeId)
  const [kind, setKind] = useState<AddPunchKind>('In')
  const [when, setWhen] = useState(`${date}T09:00`)
  const [amount, setAmount] = useState('')
  const [hours, setHours] = useState('')
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)

  async function handleSubmit() {
    setError(null)
    if (!reason.trim()) {
      setError('A reason is required.')
      return
    }
    if (kind === 'FixedDollar' && !amount) {
      setError('Enter an amount.')
      return
    }
    if (kind === 'FixedHours' && !hours) {
      setError('Enter hours.')
      return
    }

    try {
      await submit.mutateAsync({
        changeKind: 'Add',
        employeeId,
        reason,
        punchTime: new Date(when).toISOString(),
        kind,
        amount: kind === 'FixedDollar' ? Number(amount) : undefined,
        hours: kind === 'FixedHours' ? Number(hours) : undefined,
      })
      onDone()
    } catch (err) {
      setError(toApiProblem(err, 'Could not submit this request.').message)
    }
  }

  return (
    <div className="flex flex-wrap items-end gap-2 rounded-md border bg-muted/30 p-2">
      <div className="space-y-1">
        <Label htmlFor={`add-when-${date}`} className="text-xs">
          When
        </Label>
        <Input
          id={`add-when-${date}`}
          type="datetime-local"
          className="h-8"
          value={when}
          onChange={(e) => setWhen(e.target.value)}
        />
      </div>
      <div className="space-y-1">
        <Label htmlFor={`add-kind-${date}`} className="text-xs">
          Kind
        </Label>
        <Select
          id={`add-kind-${date}`}
          className="h-8 w-32"
          value={kind}
          onChange={(e) => setKind(e.target.value as AddPunchKind)}
        >
          <option value="In">Clock In</option>
          <option value="Out">Clock Out</option>
          <option value="FixedDollar">Fixed $</option>
          <option value="FixedHours">Fixed hours</option>
        </Select>
      </div>
      {kind === 'FixedDollar' && (
        <div className="space-y-1">
          <Label htmlFor={`add-amount-${date}`} className="text-xs">
            Amount
          </Label>
          <Input
            id={`add-amount-${date}`}
            type="number"
            step="0.01"
            className="h-8 w-24"
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
          />
        </div>
      )}
      {kind === 'FixedHours' && (
        <div className="space-y-1">
          <Label htmlFor={`add-hours-${date}`} className="text-xs">
            Hours
          </Label>
          <Input
            id={`add-hours-${date}`}
            type="number"
            step="0.25"
            className="h-8 w-20"
            value={hours}
            onChange={(e) => setHours(e.target.value)}
          />
        </div>
      )}
      <div className="flex-1 space-y-1">
        <Label htmlFor={`add-reason-${date}`} className="text-xs">
          Reason
        </Label>
        <Input
          id={`add-reason-${date}`}
          className="h-8"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder="e.g. forgot to clock in"
        />
      </div>
      <Button size="sm" disabled={submit.isPending} onClick={() => void handleSubmit()}>
        {submit.isPending ? 'Submitting…' : 'Submit'}
      </Button>
      <Button size="sm" variant="outline" onClick={onDone}>
        Cancel
      </Button>
      {error && <p className="w-full text-xs text-destructive">{error}</p>}
    </div>
  )
}
