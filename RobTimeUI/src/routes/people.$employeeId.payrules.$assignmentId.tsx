import { useState } from 'react'
import { createFileRoute, Link, useNavigate } from '@tanstack/react-router'
import { useEmployee } from '@/features/employees/queries'
import { usePayRules } from '@/features/payRules/queries'
import {
  useDeletePayRuleAssignment,
  usePayRuleAssignments,
  useUpdatePayRuleAssignment,
} from '@/features/payRuleAssignments/queries'
import { PayRuleAssignmentForm } from '@/features/payRuleAssignments/PayRuleAssignmentForm'
import { toApiProblem } from '@/lib/problem'
import { Button } from '@/components/ui/button'

export const Route = createFileRoute('/people/$employeeId/payrules/$assignmentId')({
  component: EditPayRuleAssignment,
})

function EditPayRuleAssignment() {
  const { employeeId, assignmentId } = Route.useParams()
  const id = Number(employeeId)
  const assignmentIdNum = Number(assignmentId)
  const navigate = useNavigate()

  const { data: employee, isPending: employeePending, isError: employeeIsError, error: employeeError } =
    useEmployee(id)
  const { data: payRules, isPending: payRulesPending } = usePayRules(
    employee ? { clientId: employee.clientId, page: 1, pageSize: 100 } : null,
  )
  const {
    data: assignments,
    isPending: assignmentsPending,
    isError: assignmentsIsError,
    error: assignmentsError,
  } = usePayRuleAssignments(id)
  const updateAssignment = useUpdatePayRuleAssignment(id, assignmentIdNum)
  const deleteAssignment = useDeletePayRuleAssignment(id)
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  const backLink = (
    <Link
      to="/people/$employeeId"
      params={{ employeeId }}
      search={{ tab: 'payrule' }}
      className="text-sm text-muted-foreground underline-offset-4 hover:underline"
    >
      ← Pay Rule
    </Link>
  )

  if (employeePending || payRulesPending || assignmentsPending) {
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

      <PayRuleAssignmentForm
        payRules={payRules?.items ?? []}
        defaultValues={{
          payRuleId: String(assignment.payRuleId),
          effectiveFrom: assignment.effectiveFrom,
          effectiveTo: assignment.effectiveTo ?? '',
        }}
        submitLabel="Save changes"
        onCancel={() =>
          void navigate({ to: '/people/$employeeId', params: { employeeId }, search: { tab: 'payrule' } })
        }
        onSubmit={async (values) => {
          await updateAssignment.mutateAsync({
            payRuleId: Number(values.payRuleId),
            effectiveFrom: values.effectiveFrom,
            effectiveTo: values.effectiveTo || undefined,
          })
          await navigate({ to: '/people/$employeeId', params: { employeeId }, search: { tab: 'payrule' } })
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
                  await navigate({ to: '/people/$employeeId', params: { employeeId }, search: { tab: 'payrule' } })
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
