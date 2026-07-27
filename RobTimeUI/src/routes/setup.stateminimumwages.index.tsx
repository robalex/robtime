import { createFileRoute, Link, useNavigate } from '@tanstack/react-router'
import { useStateMinimumWages } from '@/features/stateMinimumWages/queries'
import { toApiProblem } from '@/lib/problem'
import { Button, buttonVariants } from '@/components/ui/button'
import { Input } from '@/components/ui/input'

const PAGE_SIZE = 25

export const Route = createFileRoute('/setup/stateminimumwages/')({
  validateSearch: (search: Record<string, unknown>): { state?: string; page?: number } => ({
    state: typeof search.state === 'string' && search.state !== '' ? search.state : undefined,
    page: typeof search.page === 'number' && search.page > 1 ? search.page : undefined,
  }),
  component: StateMinimumWagesIndex,
})

function StateMinimumWagesIndex() {
  const { state, page } = Route.useSearch()
  const navigate = useNavigate({ from: Route.fullPath })
  const currentPage = page ?? 1

  const { data, isPending, isError, error } = useStateMinimumWages({ state, page: currentPage, pageSize: PAGE_SIZE })
  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between gap-4">
        <div className="space-y-1">
          <Link to="/setup" className="text-sm text-muted-foreground underline-offset-4 hover:underline">
            ← Setup
          </Link>
          <h1 className="text-2xl font-semibold tracking-tight">State minimum wages</h1>
          <p className="text-muted-foreground">
            Shared reference data across every client — not specific to the selected client.
          </p>
        </div>
        <Link to="/setup/stateminimumwages/new" className={buttonVariants()}>
          New rate
        </Link>
      </div>

      <Input
        placeholder="Filter by state…"
        defaultValue={state ?? ''}
        onChange={(event) =>
          void navigate({ search: { state: event.target.value || undefined, page: undefined }, replace: true })
        }
        className="max-w-xs"
      />

      {isError && (
        <p className="text-sm text-destructive">
          {toApiProblem(error, 'Could not load state minimum wages.').message}
        </p>
      )}

      {isPending ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : data && data.items.length > 0 ? (
        <div className="overflow-x-auto rounded-lg border">
          <table className="w-full text-sm">
            <thead className="border-b bg-muted/50 text-left">
              <tr>
                <th className="px-4 py-2 font-medium">State</th>
                <th className="px-4 py-2 font-medium">Effective from</th>
                <th className="px-4 py-2 font-medium">Effective to</th>
                <th className="px-4 py-2 font-medium">Amount</th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((wage) => (
                <tr key={wage.id} className="border-b last:border-0 hover:bg-muted/40">
                  <td className="px-4 py-2">
                    <Link
                      to="/setup/stateminimumwages/$stateMinimumWageId"
                      params={{ stateMinimumWageId: String(wage.id) }}
                      className="font-medium underline-offset-4 hover:underline"
                    >
                      {wage.state}
                    </Link>
                  </td>
                  <td className="px-4 py-2 text-muted-foreground">{wage.effectiveFrom}</td>
                  <td className="px-4 py-2 text-muted-foreground">{wage.effectiveTo ?? '—'}</td>
                  <td className="px-4 py-2 tabular-nums text-muted-foreground">${wage.amount.toFixed(2)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <p className="text-sm text-muted-foreground">
          {state ? `No rates match "${state}".` : 'No state minimum wage rates yet.'}
        </p>
      )}

      {totalPages > 1 && (
        <div className="flex items-center gap-3">
          <Button
            variant="outline"
            size="sm"
            disabled={currentPage <= 1}
            onClick={() => void navigate({ search: { state, page: currentPage - 1 } })}
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
            onClick={() => void navigate({ search: { state, page: currentPage + 1 } })}
          >
            Next
          </Button>
        </div>
      )}
    </div>
  )
}
