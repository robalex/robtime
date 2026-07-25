import { createFileRoute, Link, useNavigate } from '@tanstack/react-router'
import { useEmployees } from '@/features/employees/queries'
import { RequiresClient } from '@/components/RequiresClient'
import { toApiProblem } from '@/lib/problem'
import { Button, buttonVariants } from '@/components/ui/button'
import { Input } from '@/components/ui/input'

const PAGE_SIZE = 25

export const Route = createFileRoute('/people/')({
  validateSearch: (search: Record<string, unknown>): { q?: string; page?: number } => ({
    q: typeof search.q === 'string' && search.q !== '' ? search.q : undefined,
    page: typeof search.page === 'number' && search.page > 1 ? search.page : undefined,
  }),
  component: PeopleIndex,
})

function PeopleIndex() {
  return <RequiresClient>{(clientId) => <EmployeeList clientId={clientId} />}</RequiresClient>
}

function EmployeeList({ clientId }: { clientId: number }) {
  const { q, page } = Route.useSearch()
  const navigate = useNavigate({ from: Route.fullPath })
  const currentPage = page ?? 1

  const { data, isPending, isError, error } = useEmployees({
    clientId,
    search: q,
    page: currentPage,
    pageSize: PAGE_SIZE,
  })

  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between gap-4">
        <div className="space-y-1">
          <h1 className="text-2xl font-semibold tracking-tight">People</h1>
          <p className="text-muted-foreground">
            {data ? `${data.totalCount} employee${data.totalCount === 1 ? '' : 's'}` : 'Employees at this client.'}
          </p>
        </div>
        <Link to="/people/new" className={buttonVariants()}>
          New employee
        </Link>
      </div>

      <Input
        placeholder="Search by name…"
        defaultValue={q ?? ''}
        onChange={(event) =>
          void navigate({ search: { q: event.target.value || undefined, page: undefined }, replace: true })
        }
        className="max-w-sm"
      />

      {isError && (
        <p className="text-sm text-destructive">
          {toApiProblem(error, 'Could not load employees.').message}
        </p>
      )}

      {isPending ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : data && data.items.length > 0 ? (
        <div className="overflow-x-auto rounded-lg border">
          <table className="w-full text-sm">
            <thead className="border-b bg-muted/50 text-left">
              <tr>
                <th className="px-4 py-2 font-medium">Name</th>
                <th className="px-4 py-2 font-medium">State</th>
                <th className="px-4 py-2 font-medium">Time zone</th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((employee) => (
                <tr key={employee.id} className="border-b last:border-0 hover:bg-muted/40">
                  <td className="px-4 py-2">
                    <Link
                      to="/people/$employeeId"
                      params={{ employeeId: String(employee.id) }}
                      className="font-medium underline-offset-4 hover:underline"
                    >
                      {employee.lastName}, {employee.firstName}
                    </Link>
                  </td>
                  <td className="px-4 py-2 text-muted-foreground">{employee.state || '—'}</td>
                  <td className="px-4 py-2 text-muted-foreground">{employee.homeTimeZoneId}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <p className="text-sm text-muted-foreground">
          {q ? `No employees match “${q}”.` : 'No employees yet.'}
        </p>
      )}

      {totalPages > 1 && (
        <div className="flex items-center gap-3">
          <Button
            variant="outline"
            size="sm"
            disabled={currentPage <= 1}
            onClick={() => void navigate({ search: { q, page: currentPage - 1 } })}
          >
            Previous
          </Button>
          <span className="text-sm text-muted-foreground">
            Page {currentPage} of {totalPages}
          </span>
          <Button
            variant="outline"
            size="sm"
            disabled={currentPage >= totalPages}
            onClick={() => void navigate({ search: { q, page: currentPage + 1 } })}
          >
            Next
          </Button>
        </div>
      )}
    </div>
  )
}
