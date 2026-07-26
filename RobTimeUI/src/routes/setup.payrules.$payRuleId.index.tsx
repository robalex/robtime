import { useState } from 'react'
import { createFileRoute, Link, useNavigate } from '@tanstack/react-router'
import {
  useCreateNewPayRuleVersion,
  useDeletePayRule,
  usePayRule,
  usePayRuleTemplates,
  useUpdatePayRule,
} from '@/features/payRules/queries'
import { usePremiumRules } from '@/features/premiumRules/queries'
import { PayRuleForm, type PayRuleFormValues } from '@/features/payRules/PayRuleForm'
import { toApiProblem } from '@/lib/problem'
import { Button, buttonVariants } from '@/components/ui/button'

export const Route = createFileRoute('/setup/payrules/$payRuleId/')({
  component: EditPayRule,
})

function EditPayRule() {
  const { payRuleId } = Route.useParams()
  const id = Number(payRuleId)
  const navigate = useNavigate()

  const { data: rule, isPending: rulePending, isError, error } = usePayRule(id)
  const { data: templates } = usePayRuleTemplates()
  const { data: premiumRules } = usePremiumRules()
  const updatePayRule = useUpdatePayRule(id)
  const deletePayRule = useDeletePayRule()
  const createNewVersion = useCreateNewPayRuleVersion(id)
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [confirmingDelete, setConfirmingDelete] = useState(false)
  const [forkError, setForkError] = useState<string | null>(null)

  const backLink = (
    <Link to="/setup/payrules" className="text-sm text-muted-foreground underline-offset-4 hover:underline">
      ← Pay rules
    </Link>
  )

  if (rulePending) {
    return <p className="text-sm text-muted-foreground">Loading…</p>
  }

  if (isError || !rule) {
    const problem = toApiProblem(error, 'Could not load this pay rule.')
    return (
      <div className="space-y-4">
        <h1 className="text-xl font-semibold">
          {problem.status === 404 ? 'Pay rule not found' : 'Could not load this pay rule'}
        </h1>
        <p className="text-sm text-muted-foreground">{problem.message}</p>
        {backLink}
      </div>
    )
  }

  // Gap F's versioning rule (PayRuleRequestValidator.IsMutable, enforced server-side too): only a
  // Draft rule can be edited or deleted in place. An Active/Superseded rule may already be
  // referenced by assignments or pay snapshots, so it's shown read-only rather than pretending an
  // edit would work — "create a new version from an Active rule" isn't built yet.
  if (rule.status !== 'Draft') {
    return (
      <div className="space-y-6">
        <div className="space-y-1">
          {backLink}
          <h1 className="text-2xl font-semibold tracking-tight">
            {rule.name} <span className="text-muted-foreground">— v{rule.version}</span>
          </h1>
        </div>
        <div className="max-w-2xl rounded-lg border p-4">
          <p className="text-sm">
            This rule is <span className="font-medium">{rule.status}</span> and can't be edited
            directly — Active and Superseded versions are never mutated in place, since{' '}
            {rule.status === 'Active' ? 'pay rule assignments' : 'pay calculation snapshots'} may
            already reference this exact version.
          </p>
          <p className="mt-2 text-sm text-muted-foreground">
            To change something, create a new version — it starts as a Draft copy of this one and
            leaves this version untouched until the new one is activated.
          </p>
        </div>
        <dl className="grid max-w-2xl grid-cols-2 gap-4 text-sm">
          <div>
            <dt className="text-muted-foreground">Template</dt>
            <dd>{rule.templateCode ?? '—'}</dd>
          </div>
          <div>
            <dt className="text-muted-foreground">Status</dt>
            <dd>{rule.status}</dd>
          </div>
          <div>
            <dt className="text-muted-foreground">Weekly overtime threshold</dt>
            <dd>{rule.weeklyOvertimeThresholdHours} hours</dd>
          </div>
          <div>
            <dt className="text-muted-foreground">State premiums</dt>
            <dd>{rule.activePremiumCodes.length > 0 ? rule.activePremiumCodes.join(', ') : 'None'}</dd>
          </div>
        </dl>

        <div className="max-w-2xl space-y-2">
          <Button
            disabled={createNewVersion.isPending}
            onClick={async () => {
              setForkError(null)
              try {
                const newVersion = await createNewVersion.mutateAsync()
                await navigate({
                  to: '/setup/payrules/$payRuleId',
                  params: { payRuleId: String(newVersion.id) },
                })
              } catch (err) {
                setForkError(toApiProblem(err, 'Could not create a new version.').message)
              }
            }}
          >
            {createNewVersion.isPending ? 'Creating…' : 'Create new version to edit'}
          </Button>
          {forkError && <p className="text-sm text-destructive">{forkError}</p>}
        </div>
      </div>
    )
  }

  const template = templates?.find((t) => t.code === rule.templateCode) ?? null

  const defaultValues: PayRuleFormValues = {
    name: rule.name,
    description: rule.description ?? '',
    workweekStartDay: rule.workweekStartDay,
    weeklyOvertimeThresholdHours: String(rule.weeklyOvertimeThresholdHours),
    hasDailyOvertime: rule.hasDailyOvertime,
    dailyOvertimeThresholdHours: String(rule.dailyOvertimeThresholdHours),
    dailyDoubletimeThresholdHours: String(rule.dailyDoubletimeThresholdHours),
    hasSeventhDayRule: rule.hasSeventhDayRule,
    roundingStrategy: rule.roundingStrategy,
    roundingIntervalMinutes: String(rule.roundingIntervalMinutes),
    roundingGraceMinutes: String(rule.roundingGraceMinutes),
    punchPairResetHours: String(rule.punchPairResetHours),
    maxShiftLengthHours: String(rule.maxShiftLengthHours),
    distanceBetweenShiftsHours: String(rule.distanceBetweenShiftsHours),
    expectedBreakLengthMinutes: String(rule.expectedBreakLengthMinutes),
    expectedLunchLengthMinutes: String(rule.expectedLunchLengthMinutes),
    shiftDateStrategy: rule.shiftDateStrategy,
    activePremiumCodes: rule.activePremiumCodes,
  }

  return (
    <div className="space-y-8">
      <div className="flex items-start justify-between gap-4">
        <div className="space-y-1">
          {backLink}
          <h1 className="text-2xl font-semibold tracking-tight">
            {rule.name} <span className="text-muted-foreground">— Draft v{rule.version}</span>
          </h1>
        </div>
        <Link
          to="/setup/payrules/$payRuleId/activate"
          params={{ payRuleId }}
          className={buttonVariants({ variant: 'outline' })}
        >
          Publish as Active…
        </Link>
      </div>

      <PayRuleForm
        template={template}
        premiumRules={premiumRules ?? []}
        defaultValues={defaultValues}
        submitLabel="Save changes"
        onCancel={() => void navigate({ to: '/setup/payrules' })}
        onSubmit={(values) =>
          updatePayRule.mutateAsync({
            name: values.name,
            description: values.description || undefined,
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
          })
        }
      />

      <div className="max-w-2xl space-y-3 border-t pt-6">
        <div className="space-y-1">
          <h2 className="text-sm font-medium">Delete this pay rule</h2>
          <p className="text-sm text-muted-foreground">
            Only Draft rules can be removed — once a rule is Active it may already govern real pay
            calculations.
          </p>
        </div>
        {confirmingDelete ? (
          <div className="flex items-center gap-2">
            <Button
              variant="destructive"
              size="sm"
              disabled={deletePayRule.isPending}
              onClick={async () => {
                setDeleteError(null)
                try {
                  await deletePayRule.mutateAsync(id)
                  await navigate({ to: '/setup/payrules' })
                } catch (err) {
                  setDeleteError(toApiProblem(err, 'Could not delete this pay rule.').message)
                }
              }}
            >
              {deletePayRule.isPending ? 'Deleting…' : 'Yes, delete'}
            </Button>
            <Button variant="outline" size="sm" onClick={() => setConfirmingDelete(false)}>
              Keep it
            </Button>
          </div>
        ) : (
          <Button variant="outline" size="sm" onClick={() => setConfirmingDelete(true)}>
            Delete pay rule
          </Button>
        )}
        {deleteError && <p className="text-sm text-destructive">{deleteError}</p>}
      </div>
    </div>
  )
}
