import { useState } from 'react'
import { createFileRoute, Link, useNavigate } from '@tanstack/react-router'
import { useEmployee } from '@/features/employees/queries'
import { usePositions } from '@/features/positions/queries'
import {
  useDeletePositionAssignment,
  usePositionAssignments,
  useUpdatePositionAssignment,
} from '@/features/positionAssignments/queries'
import { PositionAssignmentForm } from '@/features/positionAssignments/PositionAssignmentForm'
import { toApiProblem } from '@/lib/problem'
import { Button } from '@/components/ui/button'

export const Route = createFileRoute('/people/$employeeId/positions/$assignmentId')({
  component: EditPositionAssignment,
})

function EditPositionAssignment() {
  const { employeeId, assignmentId } = Route.useParams()
  const id = Number(employeeId)
  const assignmentIdNum = Number(assignmentId)
  const navigate = useNavigate()

  const { data: employee, isPending: employeePending, isError: employeeIsError, error: employeeError } =
    useEmployee(id)
  const { data: positions, isPending: positionsPending } = usePositions(
    employee ? { clientId: employee.clientId, page: 1, pageSize: 100 } : null,
  )
  const {
    data: assignments,
    isPending: assignmentsPending,
    isError: assignmentsIsError,
    error: assignmentsError,
  } = usePositionAssignments(id)
  const updateAssignment = useUpdatePositionAssignment(id, assignmentIdNum)
  const deleteAssignment = useDeletePositionAssignment(id)
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  const backLink = (
    <Link
      to="/people/$employeeId"
      params={{ employeeId }}
      search={{ tab: 'positions' }}
      className="text-sm text-muted-foreground underline-offset-4 hover:underline"
    >
      ← Positions & Rates
    </Link>
  )

  if (employeePending || positionsPending || assignmentsPending) {
    return <p className="text-sm text-muted-foreground">Loading…</p>
  }

  if (employeeIsError || assignmentsIsError || !employee) {
    const problem = toApiProblem(employeeError ?? assignmentsError, 'Could not load this assignment.')
    return (
      <div className="space-y-4">
        <h1 className="text-xl font-semibold">Could not load this assignment</h1>
        <p className="text-sm text-muted-foreground">{problem.message}</p>
        {backLink}
      </div>
    )
  }

  const assignment = assignments?.find((a) => a.id === assignmentIdNum)

  if (!assignment) {
    return (
      <div className="space-y-4">
        <h1 className="text-xl font-semibold">Assignment not found</h1>
        {backLink}
      </div>
    )
  }

  return (
    <div className="space-y-8">
      <div className="space-y-1">
        {backLink}
        <h1 className="text-2xl font-semibold tracking-tight">
          Change effective — {employee.firstName} {employee.lastName}
        </h1>
      </div>

      <PositionAssignmentForm
        positions={positions?.items ?? []}
        defaultValues={{
          positionId: String(assignment.positionId),
          effectiveFrom: assignment.effectiveFrom,
          effectiveTo: assignment.effectiveTo ?? '',
          rate: assignment.rate != null ? String(assignment.rate) : '',
        }}
        submitLabel="Save changes"
        onCancel={() =>
          void navigate({ to: '/people/$employeeId', params: { employeeId }, search: { tab: 'positions' } })
        }
        onSubmit={async (values) => {
          await updateAssignment.mutateAsync({
            positionId: Number(values.positionId),
            effectiveFrom: values.effectiveFrom,
            effectiveTo: values.effectiveTo || undefined,
            rate: values.rate ? Number(values.rate) : undefined,
          })
          await navigate({ to: '/people/$employeeId', params: { employeeId }, search: { tab: 'positions' } })
        }}
      />

      <div className="max-w-md space-y-3 border-t pt-6">
        <div className="space-y-1">
          <h2 className="text-sm font-medium">Delete this assignment</h2>
          <p className="text-sm text-muted-foreground">
            Removes this period entirely, rather than ending it — use "Effective to" above if the
            assignment genuinely happened and later ended.
          </p>
        </div>
        {confirmingDelete ? (
          <div className="flex items-center gap-2">
            <Button
              variant="destructive"
              size="sm"
              disabled={deleteAssignment.isPending}
              onClick={async () => {
                setDeleteError(null)
                try {
                  await deleteAssignment.mutateAsync(assignmentIdNum)
                  await navigate({ to: '/people/$employeeId', params: { employeeId }, search: { tab: 'positions' } })
                } catch (err) {
                  setDeleteError(toApiProblem(err, 'Could not delete this assignment.').message)
                }
              }}
            >
              {deleteAssignment.isPending ? 'Deleting…' : 'Yes, delete'}
            </Button>
            <Button variant="outline" size="sm" onClick={() => setConfirmingDelete(false)}>
              Keep it
            </Button>
          </div>
        ) : (
          <Button variant="outline" size="sm" onClick={() => setConfirmingDelete(true)}>
            Delete assignment
          </Button>
        )}
        {deleteError && <p className="text-sm text-destructive">{deleteError}</p>}
      </div>
    </div>
  )
}
