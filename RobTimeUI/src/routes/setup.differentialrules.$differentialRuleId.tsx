import { useState } from 'react'
import { createFileRoute, Link, useNavigate } from '@tanstack/react-router'
import {
  useDeleteDifferentialRule,
  useDifferentialRule,
  useUpdateDifferentialRule,
  type DifferentialRule,
} from '@/features/differentialRules/queries'
import { DifferentialRuleForm, type DifferentialRuleFormValues } from '@/features/differentialRules/DifferentialRuleForm'
import { DAYS_OF_WEEK } from '@/features/differentialRules/formSchema'
import { toApiProblem } from '@/lib/problem'
import { Button } from '@/components/ui/button'

export const Route = createFileRoute('/setup/differentialrules/$differentialRuleId')({
  component: EditDifferentialRule,
})

// The wire IsoDayOfWeek can be "None" (NodaTime's default enum value) for a rule that's never used
// ConsecutiveDayRange — never a value the form itself produces, so it falls back to Monday here.
function toFormDayOfWeek(day: string): (typeof DAYS_OF_WEEK)[number] {
  return (DAYS_OF_WEEK as readonly string[]).includes(day) ? (day as (typeof DAYS_OF_WEEK)[number]) : 'Monday'
}

function toFormValues(rule: DifferentialRule): DifferentialRuleFormValues {
  return {
    code: rule.code,
    dayScheduleMode: rule.dayScheduleMode,
    daysOfWeek: rule.daysOfWeek.map(toFormDayOfWeek),
    dayOfWeekRangeStart: toFormDayOfWeek(rule.dayOfWeekRangeStart),
    dayOfWeekRangeEnd: toFormDayOfWeek(rule.dayOfWeekRangeEnd),
    specificDates: rule.specificDates,
    allDay: rule.isAllDay,
    windowStart: rule.windowStart,
    windowEnd: rule.windowEnd,
    adjustmentType: rule.adjustmentType,
    adjustmentValue: rule.adjustmentValue,
    minHoursInWindow: rule.minHoursInWindow,
    minHoursInRange: rule.minHoursInRange,
    exclusivityGroup: rule.exclusivityGroup ?? '',
  }
}

function EditDifferentialRule() {
  const { differentialRuleId } = Route.useParams()
  const id = Number(differentialRuleId)
  const navigate = useNavigate()

  const { data: rule, isPending, isError, error } = useDifferentialRule(id)
  const updateDifferentialRule = useUpdateDifferentialRule(id)
  const deleteDifferentialRule = useDeleteDifferentialRule()
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  if (isPending) {
    return <p className="text-sm text-muted-foreground">Loading…</p>
  }

  if (isError) {
    const problem = toApiProblem(error, 'Could not load this differential rule.')
    return (
      <div className="space-y-4">
        <h1 className="text-xl font-semibold">
          {problem.status === 404 ? 'Differential rule not found' : 'Could not load this differential rule'}
        </h1>
        <p className="text-sm text-muted-foreground">{problem.message}</p>
        <Link to="/setup/differentialrules" className="text-sm underline underline-offset-4">
          Back to differential rules
        </Link>
      </div>
    )
  }

  return (
    <div className="space-y-8">
      <div className="space-y-1">
        <Link
          to="/setup/differentialrules"
          className="text-sm text-muted-foreground underline-offset-4 hover:underline"
        >
          ← Differential rules
        </Link>
        <h1 className="text-2xl font-semibold tracking-tight">{rule.code}</h1>
      </div>

      <DifferentialRuleForm
        defaultValues={toFormValues(rule)}
        submitLabel="Save changes"
        onCancel={() => void navigate({ to: '/setup/differentialrules' })}
        onSubmit={(values) =>
          updateDifferentialRule.mutateAsync({
            code: values.code,
            dayScheduleMode: values.dayScheduleMode,
            daysOfWeek: values.daysOfWeek,
            dayOfWeekRangeStart: values.dayOfWeekRangeStart,
            dayOfWeekRangeEnd: values.dayOfWeekRangeEnd,
            specificDates: values.specificDates,
            windowStart: values.windowStart,
            windowEnd: values.windowEnd,
            adjustmentType: values.adjustmentType,
            adjustmentValue: values.adjustmentValue,
            minHoursInWindow: values.minHoursInWindow,
            minHoursInRange: values.minHoursInRange,
            exclusivityGroup: values.exclusivityGroup || undefined,
          })
        }
      />

      <div className="max-w-2xl space-y-3 border-t pt-6">
        <div className="space-y-1">
          <h2 className="text-sm font-medium">Delete this differential rule</h2>
          <p className="text-sm text-muted-foreground">
            Soft-deleted. If a pay rule still lists this code as active, deletion is blocked until
            it's removed from that pay rule first.
          </p>
        </div>
        {confirmingDelete ? (
          <div className="flex items-center gap-2">
            <Button
              variant="destructive"
              size="sm"
              disabled={deleteDifferentialRule.isPending}
              onClick={async () => {
                setDeleteError(null)
                try {
                  await deleteDifferentialRule.mutateAsync(id)
                  await navigate({ to: '/setup/differentialrules' })
                } catch (err) {
                  setDeleteError(toApiProblem(err, 'Could not delete this differential rule.').message)
                }
              }}
            >
              {deleteDifferentialRule.isPending ? 'Deleting…' : 'Yes, delete'}
            </Button>
            <Button variant="outline" size="sm" onClick={() => setConfirmingDelete(false)}>
              Keep it
            </Button>
          </div>
        ) : (
          <Button variant="outline" size="sm" onClick={() => setConfirmingDelete(true)}>
            Delete differential rule
          </Button>
        )}
        {deleteError && <p className="text-sm text-destructive">{deleteError}</p>}
      </div>
    </div>
  )
}
