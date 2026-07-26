import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { DifferentialRuleForm, type DifferentialRuleFormValues } from '@/features/differentialRules/DifferentialRuleForm'
import { DEFAULT_DIFFERENTIAL_RULE_FORM_VALUES } from '@/features/differentialRules/formSchema'
import { useCreateDifferentialRule } from '@/features/differentialRules/queries'
import { RequiresClient } from '@/components/RequiresClient'

export const Route = createFileRoute('/setup/differentialrules/new')({
  component: NewDifferentialRule,
})

function NewDifferentialRule() {
  return <RequiresClient>{(clientId) => <NewDifferentialRuleForm clientId={clientId} />}</RequiresClient>
}

function toRequestBody(clientId: number, values: DifferentialRuleFormValues) {
  return {
    clientId,
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
  }
}

function NewDifferentialRuleForm({ clientId }: { clientId: number }) {
  const navigate = useNavigate()
  const createDifferentialRule = useCreateDifferentialRule()

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold tracking-tight">New differential rule</h1>
      <DifferentialRuleForm
        defaultValues={DEFAULT_DIFFERENTIAL_RULE_FORM_VALUES}
        submitLabel="Create differential rule"
        onCancel={() => void navigate({ to: '/setup/differentialrules' })}
        onSubmit={async (values) => {
          await createDifferentialRule.mutateAsync(toRequestBody(clientId, values))
          await navigate({ to: '/setup/differentialrules' })
        }}
      />
    </div>
  )
}
