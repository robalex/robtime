import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { PayrollEmployeeIdentifierForm } from '@/features/payrollEmployeeIdentifiers/PayrollEmployeeIdentifierForm'
import { DEFAULT_PAYROLL_EMPLOYEE_IDENTIFIER_FORM_VALUES } from '@/features/payrollEmployeeIdentifiers/formSchema'
import { useCreatePayrollEmployeeIdentifier } from '@/features/payrollEmployeeIdentifiers/queries'
import { usePayrollExportProfile } from '@/features/payrollExportProfiles/queries'
import { useEmployees } from '@/features/employees/queries'
import { toApiProblem } from '@/lib/problem'

export const Route = createFileRoute('/setup/payrollexportprofiles/$profileId/identifiers/new')({
  component: NewEmployeeIdentifier,
})

function NewEmployeeIdentifier() {
  const { profileId } = Route.useParams()
  const profileIdNum = Number(profileId)
  const navigate = useNavigate()

  const { data: profile, isPending: profilePending, isError, error } = usePayrollExportProfile(profileIdNum)
  // A generous flat page, same simplification as other lookup selects in this app — this client's
  // employee roster is small enough that a page of 200 covers it without needing search/paging here.
  const { data: employeePage, isPending: employeesPending } = useEmployees(
    profile ? { clientId: profile.clientId, page: 1, pageSize: 200 } : null,
  )
  const createIdentifier = useCreatePayrollEmployeeIdentifier(profileIdNum)

  const backToProfile = () =>
    navigate({
      to: '/setup/payrollexportprofiles/$profileId',
      params: { profileId },
      search: { tab: 'identifiers' },
    })

  if (isError) {
    return (
      <p className="text-sm text-destructive">
        {toApiProblem(error, 'Could not load this payroll export profile.').message}
      </p>
    )
  }

  if (profilePending || employeesPending) {
    return <p className="text-sm text-muted-foreground">Loading…</p>
  }

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold tracking-tight">New employee identifier</h1>
      <PayrollEmployeeIdentifierForm
        employees={employeePage?.items ?? []}
        defaultValues={DEFAULT_PAYROLL_EMPLOYEE_IDENTIFIER_FORM_VALUES}
        submitLabel="Create identifier"
        onCancel={() => void backToProfile()}
        onSubmit={async (values) => {
          await createIdentifier.mutateAsync({
            employeeId: values.employeeId!,
            externalEmployeeId: values.externalEmployeeId,
          })
          await backToProfile()
        }}
      />
    </div>
  )
}
