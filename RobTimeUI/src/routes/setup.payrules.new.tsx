import { createFileRoute, Link, useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import { useCreatePayRule, usePayRuleTemplates, type PayRuleTemplate } from '@/features/payRules/queries'
import { usePremiumRules } from '@/features/premiumRules/queries'
import { useDifferentialRules } from '@/features/differentialRules/queries'
import { PayRuleForm, type PayRuleFormValues } from '@/features/payRules/PayRuleForm'
import { RequiresClient } from '@/components/RequiresClient'
import { toApiProblem } from '@/lib/problem'
import { Card, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'

export const Route = createFileRoute('/setup/payrules/new')({
  component: NewPayRule,
})

function NewPayRule() {
  return <RequiresClient>{(clientId) => <NewPayRuleFlow clientId={clientId} />}</RequiresClient>
}

// §6 Rule 3: creating a pay rule must start with a template — there is no blank-slate option. Once
// picked, every field (including the ones the template set) is editable, so this is a one-way door
// into the full editor, not a constraint on it.
function NewPayRuleFlow({ clientId }: { clientId: number }) {
  const navigate = useNavigate()
  const { data: templates, isPending: templatesPending, isError, error } = usePayRuleTemplates()
  const { data: premiumRules } = usePremiumRules()
  const { data: differentialRules } = useDifferentialRules({ clientId, page: 1, pageSize: 100 })
  const createPayRule = useCreatePayRule()
  const [selectedTemplate, setSelectedTemplate] = useState<PayRuleTemplate | null>(null)

  if (templatesPending) {
    return <p className="text-sm text-muted-foreground">Loading…</p>
  }

  if (isError || !templates) {
    return (
      <p className="text-sm text-destructive">
        {toApiProblem(error, 'Could not load pay rule templates.').message}
      </p>
    )
  }

  if (!selectedTemplate) {
    return (
      <div className="space-y-6">
        <div className="space-y-1">
          <Link to="/setup/payrules" className="text-sm text-muted-foreground underline-offset-4 hover:underline">
            ← Pay rules
          </Link>
          <h1 className="text-2xl font-semibold tracking-tight">Choose a template</h1>
          <p className="text-muted-foreground">
            Every pay rule starts from a jurisdiction template. Every field — including the ones the
            template sets — stays editable afterward.
          </p>
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          {templates.map((template) => (
            <button key={template.code} type="button" onClick={() => setSelectedTemplate(template)} className="text-left">
              <Card className="h-full transition-colors hover:border-ring">
                <CardHeader>
                  <CardTitle>{template.name}</CardTitle>
                  <CardDescription>{template.description}</CardDescription>
                </CardHeader>
              </Card>
            </button>
          ))}
        </div>
      </div>
    )
  }

  const defaultValues: PayRuleFormValues = {
    name: '',
    description: '',
    workweekStartDay: 'Sunday',
    weeklyOvertimeThresholdHours: String(selectedTemplate.weeklyOvertimeThresholdHours),
    hasDailyOvertime: selectedTemplate.hasDailyOvertime,
    dailyOvertimeThresholdHours: String(selectedTemplate.dailyOvertimeThresholdHours),
    dailyDoubletimeThresholdHours: String(selectedTemplate.dailyDoubletimeThresholdHours),
    hasSeventhDayRule: selectedTemplate.hasSeventhDayRule,
    roundingStrategy: 'None',
    roundingIntervalMinutes: '',
    roundingGraceMinutes: '',
    punchPairResetHours: '',
    maxShiftLengthHours: '',
    distanceBetweenShiftsHours: '',
    expectedBreakLengthMinutes: '',
    expectedLunchLengthMinutes: '',
    shiftDateStrategy: 'FirstPunchLocalDate',
    activePremiumCodes: [...selectedTemplate.activePremiumCodes],
    activeDifferentialCodes: [],
  }

  return (
    <div className="space-y-6">
      <div className="space-y-1">
        <button
          type="button"
          onClick={() => setSelectedTemplate(null)}
          className="text-sm text-muted-foreground underline-offset-4 hover:underline"
        >
          ← Choose a different template
        </button>
        <h1 className="text-2xl font-semibold tracking-tight">New pay rule — {selectedTemplate.name}</h1>
      </div>

      <PayRuleForm
        template={selectedTemplate}
        premiumRules={premiumRules ?? []}
        differentialRules={differentialRules?.items ?? []}
        defaultValues={defaultValues}
        submitLabel="Create pay rule"
        onCancel={() => void navigate({ to: '/setup/payrules' })}
        onSubmit={async (values) => {
          await createPayRule.mutateAsync({
            clientId,
            name: values.name,
            description: values.description || undefined,
            templateCode: selectedTemplate.code,
            templateVersion: selectedTemplate.version,
            workweekStartDay: values.workweekStartDay as never,
            weeklyOvertimeThresholdHours: Number(values.weeklyOvertimeThresholdHours),
            hasDailyOvertime: values.hasDailyOvertime,
            dailyOvertimeThresholdHours: values.dailyOvertimeThresholdHours
              ? Number(values.dailyOvertimeThresholdHours)
              : undefined,
            dailyDoubletimeThresholdHours: values.dailyDoubletimeThresholdHours
              ? Number(values.dailyDoubletimeThresholdHours)
              : undefined,
            hasSeventhDayRule: values.hasSeventhDayRule,
            roundingStrategy: values.roundingStrategy as never,
            roundingIntervalMinutes: values.roundingIntervalMinutes
              ? Number(values.roundingIntervalMinutes)
              : undefined,
            roundingGraceMinutes: values.roundingGraceMinutes ? Number(values.roundingGraceMinutes) : undefined,
            punchPairResetHours: values.punchPairResetHours ? Number(values.punchPairResetHours) : undefined,
            maxShiftLengthHours: values.maxShiftLengthHours ? Number(values.maxShiftLengthHours) : undefined,
            distanceBetweenShiftsHours: values.distanceBetweenShiftsHours
              ? Number(values.distanceBetweenShiftsHours)
              : undefined,
            expectedBreakLengthMinutes: values.expectedBreakLengthMinutes
              ? Number(values.expectedBreakLengthMinutes)
              : undefined,
            expectedLunchLengthMinutes: values.expectedLunchLengthMinutes
              ? Number(values.expectedLunchLengthMinutes)
              : undefined,
            shiftDateStrategy: values.shiftDateStrategy as never,
            activePremiumCodes: values.activePremiumCodes,
            activeDifferentialCodes: values.activeDifferentialCodes,
          })
          await navigate({ to: '/setup/payrules' })
        }}
      />
    </div>
  )
}
