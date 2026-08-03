import { useState } from 'react'
import { createFileRoute, Link, useNavigate } from '@tanstack/react-router'
import {
  useDeletePayrollEmployeeIdentifier,
  usePayrollEmployeeIdentifiers,
  useUpdatePayrollEmployeeIdentifier,
} from '@/features/payrollEmployeeIdentifiers/queries'
import {
  PayrollEmployeeIdentifierForm,
  type PayrollEmployeeIdentifierFormValues,
} from '@/features/payrollEmployeeIdentifiers/PayrollEmployeeIdentifierForm'
import { usePayrollExportProfile } from '@/features/payrollExportProfiles/queries'
import { useEmployees } from '@/features/employees/queries'
import { toApiProblem } from '@/lib/problem'
import { Button } from '@/components/ui/button'

export const Route = createFileRoute('/setup/payrollexportprofiles/$profileId/identifiers/$identifierId')({
  component: EditEmployeeIdentifier,
})

function EditEmployeeIdentifier() {
  const { profileId, identifierId } = Route.useParams()
  const profileIdNum = Number(profileId)
  const identifierIdNum = Number(identifierId)
  const navigate = useNavigate()

  const { data: profile } = usePayrollExportProfile(profileIdNum)
  const { data: identifiers, isPending, isError, error } = usePayrollEmployeeIdentifiers(profileIdNum)
  const { data: employeePage } = useEmployees(
    profile ? { clientId: profile.clientId, page: 1, pageSize: 200 } : null,
  )
  const updateIdentifier = useUpdatePayrollEmployeeIdentifier(profileIdNum, identifierIdNum)
  const deleteIdentifier = useDeletePayrollEmployeeIdentifier(profileIdNum)
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  const backToProfile = () =>
    navigate({
      to: '/setup/payrollexportprofiles/$profileId',
      params: { profileId },
      search: { tab: 'identifiers' },
    })

  if (isPending) {
    return <p className="text-sm text-muted-foreground">Loading…</p>
  }

  if (isError) {
    return (
      <p className="text-sm text-destructive">
        {toApiProblem(error, 'Could not load employee identifiers.').message}
      </p>
    )
  }

  const identifier = identifiers.find((i) => i.id === identifierIdNum)

  if (!identifier) {
    return (
      <div className="space-y-4">
        <h1 className="text-xl font-semibold">Identifier not found</h1>
        <Link
          to="/setup/payrollexportprofiles/$profileId"
          params={{ profileId }}
          search={{ tab: 'identifiers' }}
          className="text-sm underline underline-offset-4"
        >
          Back to employee identifiers
        </Link>
      </div>
    )
  }

  const employee = employeePage?.items.find((e) => e.id === identifier.employeeId)
  const lockedEmployeeName = employee ? `${employee.firstName} ${employee.lastName}` : `Employee #${identifier.employeeId}`

  const defaultValues: PayrollEmployeeIdentifierFormValues = {
    employeeId: undefined,
    externalEmployeeId: identifier.externalEmployeeId,
  }

  return (
    <div className="space-y-8">
      <div className="space-y-1">
        <Link
          to="/setup/payrollexportprofiles/$profileId"
          params={{ profileId }}
          search={{ tab: 'identifiers' }}
          className="text-sm text-muted-foreground underline-offset-4 hover:underline"
        >
          ← Employee identifiers
        </Link>
        <h1 className="text-2xl font-semibold tracking-tight">{lockedEmployeeName}</h1>
      </div>

      <PayrollEmployeeIdentifierForm
        lockedEmployeeName={lockedEmployeeName}
        defaultValues={defaultValues}
        submitLabel="Save changes"
        onCancel={() => void backToProfile()}
        onSubmit={(values) => updateIdentifier.mutateAsync({ externalEmployeeId: values.externalEmployeeId })}
      />

      <div className="max-w-xl space-y-3 border-t pt-6">
        <div className="space-y-1">
          <h2 className="text-sm font-medium">Delete this identifier</h2>
        </div>
        {confirmingDelete ? (
          <div className="flex items-center gap-2">
            <Button
              variant="destructive"
              size="sm"
              disabled={deleteIdentifier.isPending}
              onClick={async () => {
                setDeleteError(null)
                try {
                  await deleteIdentifier.mutateAsync(identifierIdNum)
                  await backToProfile()
                } catch (err) {
                  setDeleteError(toApiProblem(err, 'Could not delete this identifier.').message)
                }
              }}
            >
              {deleteIdentifier.isPending ? 'Deleting…' : 'Yes, delete'}
            </Button>
            <Button variant="outline" size="sm" onClick={() => setConfirmingDelete(false)}>
              Keep it
            </Button>
          </div>
        ) : (
          <Button variant="outline" size="sm" onClick={() => setConfirmingDelete(true)}>
            Delete identifier
          </Button>
        )}
        {deleteError && <p className="text-sm text-destructive">{deleteError}</p>}
      </div>
    </div>
  )
}
