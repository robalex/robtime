import { createFileRoute, Link, useNavigate } from '@tanstack/react-router'
import { useEmployee } from '@/features/employees/queries'
import { usePayRules } from '@/features/payRules/queries'
import { useCreatePayRuleAssignment } from '@/features/payRuleAssignments/queries'
import { PayRuleAssignmentForm } from '@/features/payRuleAssignments/PayRuleAssignmentForm'
import { toApiProblem } from '@/lib/problem'
import { toWireLocalDate, todayLocalDate } from '@/lib/dates'

export const Route = createFileRoute('/people/$employeeId/payrules/new')({
  component: NewPayRuleAssignment,
})

function NewPayRuleAssignment() {
  const { employeeId } = Route.useParams()
  const id = Number(employeeId)
  const navigate = useNavigate()

  const { data: employee, isPending: employeePending, isError, error } = useEmployee(id)
  // Pay rules are a small, bounded set per client — a single page of 100 is plenty (same call as
  // the eventual Pay Rules list screen will use).
  const { data: payRules, isPending: payRulesPending } = usePayRules(
    employee ? { clientId: employee.clientId, page: 1, pageSize: 100 } : null,
  )
  const createAssignment = useCreatePayRuleAssignment(id)

  if (employeePending || payRulesPending) {
    return <p className="text-sm text-muted-foreground">Loading…</p>
  }

  if (isError || !employee) {
    const problem = toApiProblem(error, 'Could not load this employee.')
    return (
      <div className="space-y-4">
        <h1 className="text-xl font-semibold">Could not load this employee</h1>
        <p className="text-sm text-muted-foreground">{problem.message}</p>
        <Link to="/people" search={{}} className="text-sm underline underline-offset-4">
          Back to people
        </Link>
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <div className="space-y-1">
        <Link
          to="/people/$employeeId"
          params={{ employeeId }}
          search={{ tab: 'payrule' }}
          className="text-sm text-muted-foreground underline-offset-4 hover:underline"
        >
          ← {employee.firstName} {employee.lastName}
        </Link>
        <h1 className="text-2xl font-semibold tracking-tight">New pay rule assignment</h1>
      </div>

      <PayRuleAssignmentForm
        payRules={payRules?.items ?? []}
        defaultValues={{
          payRuleId: '',
          effectiveFrom: toWireLocalDate(todayLocalDate()),
          effectiveTo: '',
        }}
        submitLabel="Create assignment"
        onCancel={() =>
          void navigate({ to: '/people/$employeeId', params: { employeeId }, search: { tab: 'payrule' } })
        }
        onSubmit={async (values) => {
          await createAssignment.mutateAsync({
            payRuleId: Number(values.payRuleId),
            effectiveFrom: values.effectiveFrom,
            effectiveTo: values.effectiveTo || undefined,
          })
          await navigate({ to: '/people/$employeeId', params: { employeeId }, search: { tab: 'payrule' } })
        }}
      />
    </div>
  )
}
