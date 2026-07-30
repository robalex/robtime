import { useMemo, useRef, useState } from 'react'
import { DayOfWeek } from '@js-joda/core'
import { createFileRoute, Link } from '@tanstack/react-router'
import { RequiresClient } from '@/components/RequiresClient'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select } from '@/components/ui/select'
import { parseLocalDate, toWireLocalDate, todayLocalDate } from '@/lib/dates'
import { toApiProblem } from '@/lib/problem'
import { useEmployees } from '@/features/employees/queries'
import { usePayRules } from '@/features/payRules/queries'
import { useHolidayCalendars } from '@/features/holidayCalendars/queries'
import { useDifferentialSandbox } from '@/features/differentialSandbox/queries'
import { buildZoneColorMap } from '@/features/differentialSandbox/zoneColors'
import { WeekGrid, type EvaluationSegmentInput } from '@/features/differentialSandbox/WeekGrid'
import { TestPunchEntry } from '@/features/differentialSandbox/TestPunchEntry'
import { blankTestPunchRow, toSandboxTestPunch, type TestPunchRow } from '@/features/differentialSandbox/testPunchRow'
import { ExplanationPanel } from '@/features/differentialSandbox/ExplanationPanel'

export const Route = createFileRoute('/setup/differentials/sandbox')({
  component: DifferentialSandboxRoute,
})

function DifferentialSandboxRoute() {
  return <RequiresClient>{(clientId) => <DifferentialSandboxPage clientId={clientId} />}</RequiresClient>
}

function mostRecentMonday() {
  const today = todayLocalDate()
  return today.minusDays(today.dayOfWeek().value() - DayOfWeek.MONDAY.value())
}

// The sandbox's own row-scoped error convention, matching punch import's "row[i].Field" and the
// resolve-local-time endpoint's per-punch errors: "testPunches[i].Field". Splits a failed Run's
// ValidationProblem back into per-row state so TestPunchEntry can show each row its own reason.
function applyTestPunchErrors(
  fieldErrors: Record<string, string>,
  rows: TestPunchRow[],
): { ambiguousKeys: Set<string>; rowErrors: Map<string, string>; otherMessage: string | null } {
  const ambiguousKeys = new Set<string>()
  const rowErrors = new Map<string, string>()
  let otherMessage: string | null = null

  for (const [key, message] of Object.entries(fieldErrors)) {
    const match = /^testPunches\[(\d+)]\.(.+)$/.exec(key)
    if (!match) {
      otherMessage ??= message
      continue
    }
    const row = rows[Number(match[1])]
    if (!row) {
      continue
    }
    if (match[2] === 'DaylightSaving') {
      ambiguousKeys.add(row.key)
    } else {
      rowErrors.set(row.key, message)
    }
  }

  return { ambiguousKeys, rowErrors, otherMessage }
}

function DifferentialSandboxPage({ clientId }: { clientId: number }) {
  const employees = useEmployees({ clientId, page: 1, pageSize: 200 })
  const payRules = usePayRules({ clientId, page: 1, pageSize: 100 })
  const holidayCalendars = useHolidayCalendars({ clientId, page: 1, pageSize: 50 })
  const sandbox = useDifferentialSandbox()
  const testPunchRowCounter = useRef(0)

  const [employeeId, setEmployeeId] = useState('')
  const [payRuleId, setPayRuleId] = useState('')
  const [holidayCalendarId, setHolidayCalendarId] = useState('')
  const [windowStart, setWindowStart] = useState<string>(() => toWireLocalDate(mostRecentMonday()))
  const [dayCount, setDayCount] = useState<7 | 14>(7)
  const [testPunchRows, setTestPunchRows] = useState<TestPunchRow[]>(() => [
    blankTestPunchRow(testPunchRowCounter, '', 'In'),
    blankTestPunchRow(testPunchRowCounter, '', 'Out'),
  ])
  const [ambiguousKeys, setAmbiguousKeys] = useState<Set<string>>(new Set())
  const [rowErrors, setRowErrors] = useState<Map<string, string>>(new Map())
  const [formError, setFormError] = useState<string | null>(null)

  const selectedEmployee = employees.data?.items.find((e) => String(e.id) === employeeId)
  const selectedPayRule = payRules.data?.items.find((r) => String(r.id) === payRuleId)

  const colorByCode = useMemo(
    () => buildZoneColorMap(selectedPayRule?.activeDifferentialCodes ?? []),
    [selectedPayRule],
  )

  const evaluationSegments = useMemo<EvaluationSegmentInput[]>(
    () =>
      (sandbox.data?.shifts ?? []).flatMap((shift) =>
        shift.evaluations.flatMap((evaluation) =>
          evaluation.segments.map((segment) => ({
            code: evaluation.code,
            outcome: evaluation.outcome,
            start: segment.start,
            end: segment.end,
          })),
        ),
      ),
    [sandbox.data],
  )

  async function run() {
    setFormError(null)
    setAmbiguousKeys(new Set())
    setRowErrors(new Map())
    if (!employeeId || !payRuleId) {
      setFormError('Choose an employee and a pay rule.')
      return
    }

    const completeRows = testPunchRows.filter((row) => row.when)
    try {
      await sandbox.mutateAsync({
        employeeId: Number(employeeId),
        payRuleId: Number(payRuleId),
        holidayCalendarId: holidayCalendarId ? Number(holidayCalendarId) : undefined,
        windowStart: parseLocalDate(windowStart).toString(),
        dayCount,
        testPunches: completeRows.map(toSandboxTestPunch),
      })
    } catch (err) {
      const problem = toApiProblem(err, 'Could not run the differential sandbox.')
      const { ambiguousKeys: nextAmbiguous, rowErrors: nextRowErrors, otherMessage } =
        applyTestPunchErrors(problem.fieldErrors, completeRows)
      setAmbiguousKeys(nextAmbiguous)
      setRowErrors(nextRowErrors)
      setFormError(
        nextAmbiguous.size > 0
          ? 'One or more test punches need a daylight-saving choice — see below.'
          : (otherMessage ?? problem.message),
      )
    }
  }

  return (
    <div className="space-y-6">
      <div className="space-y-1">
        <Link to="/setup" className="text-sm text-muted-foreground underline-offset-4 hover:underline">
          ← Setup
        </Link>
        <h1 className="text-2xl font-semibold tracking-tight">Differential sandbox</h1>
        <p className="text-muted-foreground">
          Pick a real employee and the pay rule to evaluate under — every differential that pay rule
          enables is drawn on the calendar below as a colored block, exactly where it would apply.
          Add test punches to see exactly which differentials would apply to them, and why.
        </p>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Setup</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-5">
            <div className="space-y-2">
              <Label htmlFor="sandbox-employee">Employee</Label>
              <Select id="sandbox-employee" value={employeeId} onChange={(e) => setEmployeeId(e.target.value)}>
                <option value="">Select…</option>
                {employees.data?.items.map((employee) => (
                  <option key={employee.id} value={employee.id}>
                    {employee.lastName}, {employee.firstName}
                  </option>
                ))}
              </Select>
            </div>
            <div className="space-y-2">
              <Label htmlFor="sandbox-payrule">Pay rule</Label>
              <Select id="sandbox-payrule" value={payRuleId} onChange={(e) => setPayRuleId(e.target.value)}>
                <option value="">Select…</option>
                {payRules.data?.items.map((rule) => (
                  <option key={rule.id} value={rule.id}>
                    {rule.name} — v{rule.version}
                  </option>
                ))}
              </Select>
            </div>
            <div className="space-y-2">
              <Label htmlFor="sandbox-holidays">Holiday calendar</Label>
              <Select id="sandbox-holidays" value={holidayCalendarId} onChange={(e) => setHolidayCalendarId(e.target.value)}>
                <option value="">None</option>
                {holidayCalendars.data?.items.map((calendar) => (
                  <option key={calendar.id} value={calendar.id}>
                    {calendar.name}
                  </option>
                ))}
              </Select>
            </div>
            <div className="space-y-2">
              <Label htmlFor="sandbox-start">Week starting</Label>
              <Input
                id="sandbox-start"
                type="date"
                value={windowStart}
                onChange={(e) => setWindowStart(e.target.value)}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="sandbox-daycount">View</Label>
              <Select
                id="sandbox-daycount"
                value={dayCount}
                onChange={(e) => setDayCount(Number(e.target.value) === 14 ? 14 : 7)}
              >
                <option value={7}>7 days</option>
                <option value={14}>14 days</option>
              </Select>
            </div>
          </div>

          {selectedPayRule && selectedPayRule.activeDifferentialCodes.length === 0 && (
            <p className="text-sm text-muted-foreground">
              This pay rule doesn't enable any differentials — nothing will be drawn on the calendar
              until you add one under Differential rules.
            </p>
          )}

          <div className="space-y-2">
            <Label>Test punches (optional)</Label>
            <TestPunchEntry
              rows={testPunchRows}
              onChange={setTestPunchRows}
              ambiguousKeys={ambiguousKeys}
              rowErrors={rowErrors}
            />
          </div>

          <Button type="button" onClick={() => void run()} disabled={sandbox.isPending}>
            {sandbox.isPending ? 'Running…' : 'Run'}
          </Button>

          {formError && <p className="text-sm text-destructive">{formError}</p>}
        </CardContent>
      </Card>

      {sandbox.data && selectedEmployee && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Calendar</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            {colorByCode.size > 0 && (
              <div className="flex flex-wrap gap-3">
                {[...colorByCode.entries()].map(([code, colorClass]) => (
                  <span key={code} className="flex items-center gap-1.5 text-xs">
                    <span className={`size-2.5 rounded-full ${colorClass.split(' ')[0]}`} />
                    {code}
                  </span>
                ))}
              </div>
            )}
            <WeekGrid
              windowStart={parseLocalDate(sandbox.data.windowStart)}
              dayCount={sandbox.data.dayCount}
              timeZone={selectedEmployee.homeTimeZoneId}
              zones={sandbox.data.zones}
              colorByCode={colorByCode}
              evaluationSegments={evaluationSegments}
            />
            {sandbox.data.zones.length === 0 && (
              <p className="text-sm text-muted-foreground">
                No differential zones fall in this window.
              </p>
            )}
          </CardContent>
        </Card>
      )}

      {sandbox.data && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Why did they apply?</CardTitle>
          </CardHeader>
          <CardContent>
            <ExplanationPanel shifts={sandbox.data.shifts} />
          </CardContent>
        </Card>
      )}
    </div>
  )
}
