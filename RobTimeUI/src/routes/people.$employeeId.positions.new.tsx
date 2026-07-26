import { createFileRoute, Link, useNavigate } from '@tanstack/react-router'
import { useEmployee } from '@/features/employees/queries'
import { usePositions } from '@/features/positions/queries'
import { useCreatePositionAssignment } from '@/features/positionAssignments/queries'
import { PositionAssignmentForm } from '@/features/positionAssignments/PositionAssignmentForm'
import { toApiProblem } from '@/lib/problem'
import { toWireLocalDate, todayLocalDate } from '@/lib/dates'

export const Route = createFileRoute('/people/$employeeId/positions/new')({
  component: NewPositionAssignment,
})

function NewPositionAssignment() {
  const { employeeId } = Route.useParams()
  const id = Number(employeeId)
  const navigate = useNavigate()

  const { data: employee, isPending: employeePending, isError, error } = useEmployee(id)
  // Positions are a small, bounded set per client — a single page of 100 is plenty (same call as
  // the Positions list screen).
  const { data: positions, isPending: positionsPending } = usePositions(
    employee ? { clientId: employee.clientId, page: 1, pageSize: 100 } : null,
  )
  const createAssignment = useCreatePositionAssignment(id)

  if (employeePending || positionsPending) {
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
          search={{ tab: 'positions' }}
          className="text-sm text-muted-foreground underline-offset-4 hover:underline"
        >
          ← {employee.firstName} {employee.lastName}
        </Link>
        <h1 className="text-2xl font-semibold tracking-tight">New position assignment</h1>
      </div>

      <PositionAssignmentForm
        positions={positions?.items ?? []}
        defaultValues={{
          positionId: '',
          effectiveFrom: toWireLocalDate(todayLocalDate()),
          effectiveTo: '',
          rate: '',
        }}
        submitLabel="Create assignment"
        onCancel={() =>
          void navigate({ to: '/people/$employeeId', params: { employeeId }, search: { tab: 'positions' } })
        }
        onSubmit={async (values) => {
          await createAssignment.mutateAsync({
            positionId: Number(values.positionId),
            effectiveFrom: values.effectiveFrom,
            effectiveTo: values.effectiveTo || undefined,
            rate: values.rate ? Number(values.rate) : undefined,
          })
          await navigate({ to: '/people/$employeeId', params: { employeeId }, search: { tab: 'positions' } })
        }}
      />
    </div>
  )
}
