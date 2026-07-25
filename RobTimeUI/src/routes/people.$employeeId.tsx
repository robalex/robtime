import { useState } from 'react'
import { createFileRoute, Link, useNavigate } from '@tanstack/react-router'
import { useDeleteEmployee, useEmployee, useUpdateEmployee } from '@/features/employees/queries'
import { EmployeeForm } from '@/features/employees/EmployeeForm'
import { toApiProblem } from '@/lib/problem'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

// Tabs from §6's screen inventory. Only Details is live; the rest name the phase that fills them in
// rather than rendering a convincing-looking empty state — a tab that looks built but does nothing
// is worse than one that says what it's waiting for.
const TABS = [
  { id: 'details', label: 'Details' },
  { id: 'positions', label: 'Positions & Rates' },
  { id: 'payrule', label: 'Pay Rule' },
  { id: 'punches', label: 'Punches' },
] as const

type TabId = (typeof TABS)[number]['id']

export const Route = createFileRoute('/people/$employeeId')({
  // The active tab is URL state, so a specific tab is linkable and survives a refresh — same
  // reasoning as the list's search params.
  validateSearch: (search: Record<string, unknown>): { tab?: TabId } => {
    const tab = TABS.find((t) => t.id === search.tab)?.id
    return tab && tab !== 'details' ? { tab } : {}
  },
  component: EmployeeDetail,
})

function EmployeeDetail() {
  const { employeeId } = Route.useParams()
  const { tab } = Route.useSearch()
  const activeTab: TabId = tab ?? 'details'
  const id = Number(employeeId)
  const navigate = useNavigate()

  const { data: employee, isPending, isError, error } = useEmployee(id)
  const updateEmployee = useUpdateEmployee(id)
  const deleteEmployee = useDeleteEmployee()
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  if (isPending) {
    return <p className="text-sm text-muted-foreground">Loading…</p>
  }

  if (isError) {
    const problem = toApiProblem(error, 'Could not load this employee.')
    return (
      <div className="space-y-4">
        <h1 className="text-xl font-semibold">
          {problem.status === 404 ? 'Employee not found' : 'Could not load this employee'}
        </h1>
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
        <Link to="/people" search={{}} className="text-sm text-muted-foreground underline-offset-4 hover:underline">
          ← People
        </Link>
        <h1 className="text-2xl font-semibold tracking-tight">
          {employee.firstName} {employee.lastName}
        </h1>
      </div>

      <div className="border-b">
        <nav className="-mb-px flex gap-4">
          {TABS.map((t) => (
            <button
              key={t.id}
              type="button"
              onClick={() =>
                void navigate({
                  to: '/people/$employeeId',
                  params: { employeeId },
                  search: t.id === 'details' ? {} : { tab: t.id },
                })
              }
              className={cn(
                'border-b-2 px-1 py-2 text-sm font-medium transition-colors',
                activeTab === t.id
                  ? 'border-foreground text-foreground'
                  : 'border-transparent text-muted-foreground hover:text-foreground',
              )}
            >
              {t.label}
            </button>
          ))}
        </nav>
      </div>

      {activeTab === 'details' && (
        <div className="space-y-8">
          <EmployeeForm
            defaultValues={{
              firstName: employee.firstName,
              lastName: employee.lastName,
              minimumWage: employee.minimumWage,
              state: employee.state,
              homeTimeZoneId: employee.homeTimeZoneId,
              middleName: employee.middleName,
              salutation: employee.salutation,
              postNominalLetters: employee.postNominalLetters,
            }}
            submitLabel="Save changes"
            onCancel={() => void navigate({ to: '/people', search: {} })}
            onSubmit={(values) =>
              updateEmployee.mutateAsync({
                firstName: values.firstName,
                lastName: values.lastName,
                minimumWage: values.minimumWage,
                state: values.state || undefined,
                homeTimeZoneId: values.homeTimeZoneId || undefined,
                middleName: values.middleName || undefined,
                salutation: values.salutation || undefined,
                postNominalLetters: values.postNominalLetters || undefined,
              })
            }
          />

          <div className="space-y-3 border-t pt-6">
            <div className="space-y-1">
              <h2 className="text-sm font-medium">Delete this employee</h2>
              <p className="text-sm text-muted-foreground">
                Soft-deleted and hidden everywhere; punch and pay history is retained.
              </p>
            </div>
            {confirmingDelete ? (
              <div className="flex items-center gap-2">
                <Button
                  variant="destructive"
                  size="sm"
                  disabled={deleteEmployee.isPending}
                  onClick={async () => {
                    setDeleteError(null)
                    try {
                      await deleteEmployee.mutateAsync(id)
                      await navigate({ to: '/people', search: {} })
                    } catch (err) {
                      setDeleteError(toApiProblem(err, 'Could not delete this employee.').message)
                    }
                  }}
                >
                  {deleteEmployee.isPending ? 'Deleting…' : 'Yes, delete'}
                </Button>
                <Button variant="outline" size="sm" onClick={() => setConfirmingDelete(false)}>
                  Keep it
                </Button>
              </div>
            ) : (
              <Button variant="outline" size="sm" onClick={() => setConfirmingDelete(true)}>
                Delete employee
              </Button>
            )}
            {deleteError && <p className="text-sm text-destructive">{deleteError}</p>}
          </div>
        </div>
      )}

      {activeTab === 'positions' && (
        <p className="text-sm text-muted-foreground">
          Effective-dated position and rate assignments land next, once the assignment API exists.
        </p>
      )}
      {activeTab === 'payrule' && (
        <p className="text-sm text-muted-foreground">Pay rule assignment lands in Phase 4.</p>
      )}
      {activeTab === 'punches' && (
        <p className="text-sm text-muted-foreground">Punches and timecards land in Phase 6.</p>
      )}
    </div>
  )
}
