import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { EmployeeForm } from '@/features/employees/EmployeeForm'
import { useCreateEmployee } from '@/features/employees/queries'
import { RequiresClient } from '@/components/RequiresClient'

export const Route = createFileRoute('/people/new')({
  component: NewEmployee,
})

function NewEmployee() {
  return <RequiresClient>{(clientId) => <NewEmployeeForm clientId={clientId} />}</RequiresClient>
}

function NewEmployeeForm({ clientId }: { clientId: number }) {
  const navigate = useNavigate()
  const createEmployee = useCreateEmployee()

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold tracking-tight">New employee</h1>
      <EmployeeForm
        submitLabel="Create employee"
        onCancel={() => void navigate({ to: '/people', search: {} })}
        onSubmit={async (values) => {
          const created = await createEmployee.mutateAsync({
            // clientId comes from the current scope, never a form field — the tenant is established
            // by the session, and letting it be typed would be both an odd UX and an authorization
            // question the server would then have to re-answer.
            clientId,
            firstName: values.firstName,
            lastName: values.lastName,
            minimumWage: values.minimumWage,
            // Blank optional fields are omitted rather than sent as "", so the server's own defaults
            // apply (§6 Rule 2).
            state: values.state || undefined,
            homeTimeZoneId: values.homeTimeZoneId || undefined,
            middleName: values.middleName || undefined,
            salutation: values.salutation || undefined,
            postNominalLetters: values.postNominalLetters || undefined,
          })
          await navigate({ to: '/people/$employeeId', params: { employeeId: String(created.id) } })
        }}
      />
    </div>
  )
}
