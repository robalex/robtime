import { useEffect, useMemo, useRef, useState } from 'react'
import { CalendarPlus, Plus, Trash2 } from 'lucide-react'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select } from '@/components/ui/select'
import { Label } from '@/components/ui/label'
import { cn } from '@/lib/utils'
import { toApiProblem } from '@/lib/problem'
import { usePositionAssignments } from '@/features/positionAssignments/queries'
import {
  useCreatePunchBatch,
  usePreviewTimecard,
  type CreatePunch,
  type DraftPunchEntry,
} from './queries'

type PunchKind = 'In' | 'Out' | 'FixedDollar' | 'FixedHours'
type BonusKind = 'Discretionary' | 'NonDiscretionary'

interface DraftRow {
  key: string
  // datetime-local string ("2026-06-01T09:00") — browser-local time, same convention Timecard.tsx
  // and ClockCard.tsx already use elsewhere in this app (see Timecard.tsx's formatTime comment).
  when: string
  kind: PunchKind
  positionId: string // '' = fall back to the default position picker below
  amount: string // FixedDollar
  hours: string // FixedHours
  bonusKind: BonusKind | ''
  countsTowardRegularRate: boolean
}

const HOURS_FORMAT = new Intl.NumberFormat('en-US', { minimumFractionDigits: 1, maximumFractionDigits: 1 })
const MONEY_FORMAT = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })

function formatHours(hours: number): string {
  return `${HOURS_FORMAT.format(hours)}h`
}

function formatMoney(amount: number): string {
  return MONEY_FORMAT.format(amount)
}

function toDateTimeLocal(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
}

function todayAt(hour: number): string {
  const d = new Date()
  d.setHours(hour, 0, 0, 0)
  return toDateTimeLocal(d)
}

function blankRow(counter: { current: number }, when: string, kind: PunchKind = 'In'): DraftRow {
  counter.current += 1
  return {
    key: `row-${counter.current}`,
    when,
    kind,
    positionId: '',
    amount: '',
    hours: '',
    bonusKind: '',
    countsTowardRegularRate: false,
  }
}

/** The row's data as a draft entry, or null if it isn't complete enough to preview/save yet —
 * an in-progress row (e.g. FixedDollar with no amount typed) is simply skipped rather than treated
 * as an error, since the grid is meant to preview "as best it can" while someone is still typing. */
function toDraftEntry(row: DraftRow, defaultPositionId: string): DraftPunchEntry | null {
  if (!row.when) {
    return null
  }
  const punchTime = new Date(row.when).toISOString()
  const positionId = row.positionId || defaultPositionId
  const base = { punchTime, positionId: positionId ? Number(positionId) : undefined }

  if (row.kind === 'FixedDollar') {
    const amount = Number(row.amount)
    if (row.amount === '' || Number.isNaN(amount)) {
      return null
    }
    return { ...base, kind: 'FixedDollar', amount, bonusKind: row.bonusKind || undefined }
  }

  if (row.kind === 'FixedHours') {
    const hours = Number(row.hours)
    if (row.hours === '' || Number.isNaN(hours)) {
      return null
    }
    return { ...base, kind: 'FixedHours', hours, countsTowardRegularRate: row.countsTowardRegularRate }
  }

  return { ...base, kind: row.kind }
}

/**
 * Phase 6.8's fast bulk-entry grid: keyboard-first punch entry for a full week (or however many
 * rows) in one pass, with a live pay preview updating as the rows are typed — the batch save at the
 * end is the only thing that actually persists anything (POST /punches/batch), so mistakes mid-entry
 * cost nothing. Mounted the same two places Timecard is (own /time page, People "Punches" tab):
 * employeeId is the only thing this needs, matching Timecard's own mounting convention.
 */
export function BulkPunchEntry({ employeeId, date }: { employeeId: number; date?: string }) {
  const rowCounter = useRef(0)
  const [rows, setRows] = useState<DraftRow[]>(() => [
    blankRow(rowCounter, todayAt(9), 'In'),
    blankRow(rowCounter, todayAt(17), 'Out'),
  ])
  const [defaultPositionId, setDefaultPositionId] = useState('')
  const [saveError, setSaveError] = useState<string | null>(null)
  const [savedCount, setSavedCount] = useState<number | null>(null)
  const focusKey = useRef<string | null>(null)

  const positions = usePositionAssignments(employeeId)
  const preview = usePreviewTimecard(employeeId, date)
  const batch = useCreatePunchBatch(employeeId)

  const draftEntries = useMemo(
    () => rows.map((row) => toDraftEntry(row, defaultPositionId)).filter((e): e is DraftPunchEntry => e !== null),
    [rows, defaultPositionId],
  )

  // Debounced live preview: fires ~400ms after the rows stop changing, not on every keystroke — a
  // preview call on every character would mean a request per keypress across a whole grid of rows.
  useEffect(() => {
    if (draftEntries.length === 0) {
      return
    }
    const timer = setTimeout(() => {
      preview.mutate(draftEntries)
    }, 400)
    return () => clearTimeout(timer)
    // preview itself is deliberately left out of the deps — depending on the whole mutation object
    // would re-run this on every preview.isPending/data change instead of only when rows change.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [draftEntries])

  useEffect(() => {
    if (!focusKey.current) {
      return
    }
    const el = document.querySelector<HTMLInputElement>(`[data-row-key="${focusKey.current}"]`)
    el?.focus()
    focusKey.current = null
  }, [rows])

  function updateRow(key: string, patch: Partial<DraftRow>) {
    setRows((prev) => prev.map((row) => (row.key === key ? { ...row, ...patch } : row)))
    setSavedCount(null)
  }

  function removeRow(key: string) {
    setRows((prev) => (prev.length > 1 ? prev.filter((row) => row.key !== key) : prev))
  }

  function addRow(after: DraftRow) {
    const next = blankRow(
      rowCounter,
      toDateTimeLocal(new Date(new Date(after.when || todayAt(9)).getTime() + 60 * 60 * 1000)),
      after.kind === 'In' ? 'Out' : after.kind === 'Out' ? 'In' : after.kind,
    )
    next.positionId = after.positionId
    focusKey.current = next.key
    setRows((prev) => [...prev, next])
  }

  // "+ Add day" is the answer to "a full week's worth of punches without leaving the keyboard" —
  // one click (or Enter) appends a ready-to-go In/Out pair for the next day, defaulting to a
  // standard 9-to-5 that most rows will only need a couple of digits changed on.
  function addDay() {
    const lastWhen = rows.length > 0 ? rows[rows.length - 1].when : ''
    const base = lastWhen ? new Date(lastWhen) : new Date()
    const nextDay = new Date(base)
    nextDay.setDate(nextDay.getDate() + 1)
    const clockIn = blankRow(rowCounter, toDateTimeLocal(setHour(nextDay, 9)), 'In')
    const clockOut = blankRow(rowCounter, toDateTimeLocal(setHour(nextDay, 17)), 'Out')
    focusKey.current = clockIn.key
    setRows((prev) => [...prev, clockIn, clockOut])
  }

  // Wired to every focusable cell in a row (including the Remove button, so Enter there adds a row
  // instead of deleting the one just filled in). One native gap: Chromium's own
  // <input type="datetime-local"> consumes a real Enter keypress internally to step between its
  // date/time segments before it ever becomes a page-visible keydown, so Enter while focus is inside
  // the When field itself is a no-op here — Tab (or a click) into Kind/Position/Value/Options still
  // reaches this handler normally.
  function handleRowKeyDown(row: DraftRow, event: React.KeyboardEvent) {
    if (event.key !== 'Enter') {
      return
    }
    const isLastRow = rows[rows.length - 1]?.key === row.key
    if (isLastRow) {
      event.preventDefault()
      addRow(row)
    }
  }

  async function handleSave() {
    setSaveError(null)
    setSavedCount(null)
    const punches: CreatePunch[] = rows
      .map((row) => toDraftEntry(row, defaultPositionId))
      .map((entry) => (entry ? ({ ...entry, employeeId } as CreatePunch) : null))
      .filter((p): p is CreatePunch => p !== null)

    if (punches.length === 0) {
      setSaveError('Enter at least one complete punch first.')
      return
    }

    try {
      const saved = await batch.mutateAsync(punches)
      setSavedCount(saved.length)
      setRows([blankRow(rowCounter, todayAt(9), 'In'), blankRow(rowCounter, todayAt(17), 'Out')])
    } catch (err) {
      setSaveError(toApiProblem(err, 'Could not save these punches.').message)
    }
  }

  const previewWeeks = preview.data?.weeks ?? []
  const previewTotals = previewWeeks.reduce(
    (acc, week) => ({
      regularHours: acc.regularHours + week.regularHours,
      overtimeHours: acc.overtimeHours + week.overtimeHours,
      doubletimeHours: acc.doubletimeHours + week.doubletimeHours,
    }),
    { regularHours: 0, overtimeHours: 0, doubletimeHours: 0 },
  )

  return (
    <Card>
      <CardHeader className="flex-row flex-wrap items-center justify-between gap-4 space-y-0">
        <CardTitle className="text-base">Fast punch entry</CardTitle>
        {positions.data && positions.data.length > 1 && (
          <div className="flex items-center gap-2">
            <Label htmlFor="default-position" className="whitespace-nowrap text-xs text-muted-foreground">
              Default position
            </Label>
            <Select
              id="default-position"
              className="h-8 w-48"
              value={defaultPositionId}
              onChange={(e) => setDefaultPositionId(e.target.value)}
            >
              <option value="">None</option>
              {positions.data.map((a) => (
                <option key={a.id} value={String(a.positionId)}>
                  {a.positionName}
                </option>
              ))}
            </Select>
          </div>
        )}
      </CardHeader>

      <CardContent className="space-y-4">
        <div className="overflow-x-auto rounded-lg border">
          <table className="w-full text-sm">
            <thead className="border-b bg-muted/50 text-left">
              <tr>
                <th className="px-3 py-2 font-medium">When</th>
                <th className="px-3 py-2 font-medium">Kind</th>
                <th className="px-3 py-2 font-medium">Position</th>
                <th className="px-3 py-2 font-medium">Value</th>
                <th className="px-3 py-2 font-medium">Options</th>
                <th className="px-3 py-2" />
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.key} className="border-b last:border-0">
                  <td className="px-3 py-1.5">
                    <Input
                      type="datetime-local"
                      data-row-key={row.key}
                      className="h-8 min-w-[190px]"
                      value={row.when}
                      onChange={(e) => updateRow(row.key, { when: e.target.value })}
                      onKeyDown={(e) => handleRowKeyDown(row, e)}
                    />
                  </td>
                  <td className="px-3 py-1.5">
                    <Select
                      className="h-8 w-32"
                      value={row.kind}
                      onChange={(e) => updateRow(row.key, { kind: e.target.value as PunchKind })}
                      onKeyDown={(e) => handleRowKeyDown(row, e)}
                    >
                      <option value="In">Clock In</option>
                      <option value="Out">Clock Out</option>
                      <option value="FixedDollar">Fixed $</option>
                      <option value="FixedHours">Fixed hours</option>
                    </Select>
                  </td>
                  <td className="px-3 py-1.5">
                    <Select
                      className="h-8 w-36"
                      value={row.positionId}
                      onChange={(e) => updateRow(row.key, { positionId: e.target.value })}
                      onKeyDown={(e) => handleRowKeyDown(row, e)}
                    >
                      <option value="">
                        {defaultPositionId
                          ? (positions.data?.find((a) => String(a.positionId) === defaultPositionId)?.positionName ?? 'Default')
                          : 'Default'}
                      </option>
                      {positions.data?.map((a) => (
                        <option key={a.id} value={String(a.positionId)}>
                          {a.positionName}
                        </option>
                      ))}
                    </Select>
                  </td>
                  <td className="px-3 py-1.5">
                    {row.kind === 'FixedDollar' && (
                      <Input
                        type="number"
                        step="0.01"
                        placeholder="Amount"
                        className="h-8 w-28"
                        value={row.amount}
                        onChange={(e) => updateRow(row.key, { amount: e.target.value })}
                        onKeyDown={(e) => handleRowKeyDown(row, e)}
                      />
                    )}
                    {row.kind === 'FixedHours' && (
                      <Input
                        type="number"
                        step="0.25"
                        placeholder="Hours"
                        className="h-8 w-24"
                        value={row.hours}
                        onChange={(e) => updateRow(row.key, { hours: e.target.value })}
                        onKeyDown={(e) => handleRowKeyDown(row, e)}
                      />
                    )}
                    {(row.kind === 'In' || row.kind === 'Out') && <span className="text-muted-foreground">—</span>}
                  </td>
                  <td className="px-3 py-1.5">
                    {row.kind === 'FixedDollar' && (
                      <Select
                        className="h-8 w-40"
                        value={row.bonusKind}
                        onChange={(e) => updateRow(row.key, { bonusKind: e.target.value as BonusKind | '' })}
                        onKeyDown={(e) => handleRowKeyDown(row, e)}
                      >
                        <option value="">Not a bonus</option>
                        <option value="Discretionary">Discretionary bonus</option>
                        <option value="NonDiscretionary">Non-discretionary bonus</option>
                      </Select>
                    )}
                    {row.kind === 'FixedHours' && (
                      <label className="flex items-center gap-1.5 whitespace-nowrap text-xs text-muted-foreground">
                        <input
                          type="checkbox"
                          checked={row.countsTowardRegularRate}
                          onChange={(e) => updateRow(row.key, { countsTowardRegularRate: e.target.checked })}
                        />
                        Counts toward rate
                      </label>
                    )}
                  </td>
                  <td className="px-3 py-1.5 text-right">
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      aria-label="Remove row"
                      className="size-8"
                      onClick={() => removeRow(row.key)}
                      // For an In/Out row (no Value/Options cells), this is where Tab naturally
                      // lands last — without this, Enter here would activate the button and delete
                      // the row someone had just finished filling in, the opposite of every other
                      // field's Enter-to-add-a-row behavior in this grid.
                      onKeyDown={(e) => handleRowKeyDown(row, e)}
                      disabled={rows.length === 1}
                    >
                      <Trash2 className="size-4" />
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex gap-2">
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => addRow(rows[rows.length - 1])}
            >
              <Plus className="size-4" /> Add row
            </Button>
            <Button type="button" variant="outline" size="sm" onClick={addDay}>
              <CalendarPlus className="size-4" /> Add day
            </Button>
          </div>

          <div className="flex items-center gap-4">
            <div className="flex items-center gap-3 text-sm tabular-nums">
              <span>{formatHours(previewTotals.regularHours)} reg</span>
              {previewTotals.overtimeHours > 0 && (
                <span className="text-amber-700 dark:text-amber-500">{formatHours(previewTotals.overtimeHours)} OT</span>
              )}
              {previewTotals.doubletimeHours > 0 && (
                <span className="text-amber-700 dark:text-amber-500">{formatHours(previewTotals.doubletimeHours)} DT</span>
              )}
              <span className={cn('font-medium', preview.isPending && 'text-muted-foreground')}>
                {formatMoney(preview.data?.grossPay ?? 0)}
              </span>
            </div>
            <Button type="button" size="sm" disabled={batch.isPending} onClick={() => void handleSave()}>
              {batch.isPending ? 'Saving…' : 'Save punches'}
            </Button>
          </div>
        </div>

        {saveError && <p className="text-sm text-destructive">{saveError}</p>}
        {savedCount !== null && (
          <p className="text-sm text-muted-foreground">
            Saved {savedCount} punch{savedCount === 1 ? '' : 'es'}.
          </p>
        )}
      </CardContent>
    </Card>
  )
}

function setHour(date: Date, hour: number): Date {
  const d = new Date(date)
  d.setHours(hour, 0, 0, 0)
  return d
}
