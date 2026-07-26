import { useState } from 'react'
import { useEmployees } from '@/features/employees/queries'
import { useRunPayRuleWhatIf, type WhatIfResponse } from './queries'
import { toWireLocalDate, todayLocalDate } from '@/lib/dates'
import { toApiProblem } from '@/lib/problem'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select } from '@/components/ui/select'

const STATUS_LABELS: Record<WhatIfResponse['shiftDiffs'][number]['status'], string> = {
  Unchanged: 'Unchanged',
  Changed: 'Changed',
  OnlyInCurrent: 'Only under current rule',
  OnlyInDraft: 'Only under this rule',
}

const STATUS_STYLES: Record<WhatIfResponse['shiftDiffs'][number]['status'], string> = {
  Unchanged: 'bg-muted text-muted-foreground',
  Changed: 'bg-primary/10 text-primary',
  OnlyInCurrent: 'bg-destructive/10 text-destructive',
  OnlyInDraft: 'bg-primary/10 text-primary',
}

interface WhatIfPanelProps {
  payRuleId: number
  clientId: number
}

// Phase 4 §7's "down payment" (UI_PLAN.md): pick one employee, pick one past period, run both pay
// rule configs synchronously, show a side-by-side line-item diff. Lives on the Draft editor page —
// the whole point is previewing this rule's effect before activating it.
export function WhatIfPanel({ payRuleId, clientId }: WhatIfPanelProps) {
  const { data: employees } = useEmployees({ clientId, page: 1, pageSize: 100 })
  const [employeeId, setEmployeeId] = useState('')
  const [periodStart, setPeriodStart] = useState<string>(() => toWireLocalDate(todayLocalDate().minusDays(7)))
  const [periodEnd, setPeriodEnd] = useState<string>(() => toWireLocalDate(todayLocalDate()))
  const [formError, setFormError] = useState<string | null>(null)
  const whatIf = useRunPayRuleWhatIf(payRuleId)

  const run = async () => {
    setFormError(null)
    if (!employeeId) {
      setFormError('Choose an employee.')
      return
    }
    try {
      await whatIf.mutateAsync({ employeeId: Number(employeeId), periodStart, periodEnd })
    } catch (err) {
      setFormError(toApiProblem(err, 'Could not run the what-if preview.').message)
    }
  }

  return (
    <div className="max-w-3xl space-y-4 rounded-lg border p-4">
      <div className="space-y-1">
        <h2 className="text-sm font-medium">What if this rule were active?</h2>
        <p className="text-sm text-muted-foreground">
          Pick an employee and a past period — this runs both the rule they're actually under and
          this draft over their real punches, and shows exactly which shifts would change.
        </p>
      </div>

      <div className="grid gap-4 sm:grid-cols-3">
        <div className="space-y-2">
          <Label htmlFor="whatif-employee">Employee</Label>
          <Select id="whatif-employee" value={employeeId} onChange={(event) => setEmployeeId(event.target.value)}>
            <option value="">Select…</option>
            {employees?.items.map((employee) => (
              <option key={employee.id} value={employee.id}>
                {employee.lastName}, {employee.firstName}
              </option>
            ))}
          </Select>
        </div>
        <div className="space-y-2">
          <Label htmlFor="whatif-periodStart">From</Label>
          <Input
            id="whatif-periodStart"
            type="date"
            value={periodStart}
            onChange={(event) => setPeriodStart(event.target.value)}
          />
        </div>
        <div className="space-y-2">
          <Label htmlFor="whatif-periodEnd">To</Label>
          <Input
            id="whatif-periodEnd"
            type="date"
            value={periodEnd}
            onChange={(event) => setPeriodEnd(event.target.value)}
          />
        </div>
      </div>

      <Button type="button" onClick={() => void run()} disabled={whatIf.isPending}>
        {whatIf.isPending ? 'Running…' : 'Run preview'}
      </Button>

      {formError && <p className="text-sm text-destructive">{formError}</p>}

      {whatIf.data && <WhatIfResultView result={whatIf.data} />}
    </div>
  )
}

function WhatIfResultView({ result }: { result: WhatIfResponse }) {
  const grossDelta = result.draft.grossPay - result.current.grossPay

  return (
    <div className="space-y-4 border-t pt-4">
      <div className="grid gap-4 sm:grid-cols-2">
        <SummaryCard title="Current" label={`${result.current.payRuleName} — v${result.current.payRuleVersion}`} summary={result.current} />
        <SummaryCard title="This rule (draft)" label={`${result.draft.payRuleName} — v${result.draft.payRuleVersion}`} summary={result.draft} />
      </div>

      <p className="text-sm font-medium">
        Gross pay {grossDelta === 0 ? 'unchanged' : grossDelta > 0 ? 'increases' : 'decreases'} by{' '}
        <span className={grossDelta === 0 ? '' : grossDelta > 0 ? 'text-primary' : 'text-destructive'}>
          ${Math.abs(grossDelta).toFixed(2)}
        </span>{' '}
        over this period.
      </p>

      {result.shiftDiffs.length === 0 ? (
        <p className="text-sm text-muted-foreground">No shifts in this period.</p>
      ) : (
        <div className="space-y-3">
          {result.shiftDiffs.map((diff) => (
            <div key={`${diff.shiftDate}-${diff.anchorPunchId}`} className="rounded-md border p-3">
              <div className="flex items-center justify-between gap-2">
                <span className="text-sm font-medium">{diff.shiftDate}</span>
                <div className="flex items-center gap-2">
                  <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${STATUS_STYLES[diff.status]}`}>
                    {STATUS_LABELS[diff.status]}
                  </span>
                  <span className="text-sm tabular-nums text-muted-foreground">
                    ${diff.currentGross.toFixed(2)} → ${diff.draftGross.toFixed(2)}
                  </span>
                </div>
              </div>

              {diff.status !== 'Unchanged' && (
                <div className="mt-3 grid gap-3 sm:grid-cols-2">
                  <LineItemList title="Current" items={diff.currentLineItems} />
                  <LineItemList title="Draft" items={diff.draftLineItems} />
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

function SummaryCard({
  title,
  label,
  summary,
}: {
  title: string
  label: string
  summary: WhatIfResponse['current']
}) {
  return (
    <div className="space-y-2 rounded-md border p-3">
      <div>
        <p className="text-xs font-medium uppercase text-muted-foreground">{title}</p>
        <p className="text-sm font-medium">{label}</p>
      </div>
      <dl className="grid grid-cols-2 gap-1 text-sm">
        <dt className="text-muted-foreground">Regular hours</dt>
        <dd className="text-right tabular-nums">{summary.regularHours.toFixed(2)}</dd>
        <dt className="text-muted-foreground">Overtime hours</dt>
        <dd className="text-right tabular-nums">{summary.overtimeHours.toFixed(2)}</dd>
        <dt className="text-muted-foreground">Doubletime hours</dt>
        <dd className="text-right tabular-nums">{summary.doubletimeHours.toFixed(2)}</dd>
        <dt className="font-medium">Gross pay</dt>
        <dd className="text-right font-medium tabular-nums">${summary.grossPay.toFixed(2)}</dd>
      </dl>
    </div>
  )
}

function LineItemList({ title, items }: { title: string; items: WhatIfResponse['shiftDiffs'][number]['currentLineItems'] }) {
  return (
    <div className="space-y-1">
      <p className="text-xs font-medium uppercase text-muted-foreground">{title}</p>
      {items.length === 0 ? (
        <p className="text-sm text-muted-foreground">—</p>
      ) : (
        <ul className="space-y-0.5 text-sm">
          {items.map((item, index) => (
            <li key={index} className="flex justify-between gap-2">
              <span>
                {item.type}
                {item.code ? ` (${item.code})` : ''}
              </span>
              <span className="tabular-nums">${item.amount.toFixed(2)}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
